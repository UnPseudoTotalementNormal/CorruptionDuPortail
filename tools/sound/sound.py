"""Sound maker: find free sound effects, shape them, harmonize their loudness, put them in FMOD, keep the credits.

  python tools/sound/sound.py search "<words>" [--max-dur S] [--min-dur S] [--license cc0,by] [-n N] [--work W]
        Freesound (API with a key, else the public search pages). Writes W/candidates.json, prints the list.
  python tools/sound/sound.py fetch <W> <freesound id>...
        Download the HQ preview + the sound's page metadata (author, license) into W/raw/, W/sources.json.
  python tools/sound/sound.py add <W> <file> --title T --author A --license L --url U [--license-url LU]
        A sound from another source (Kenney, Sonniss, OpenGameArt…): copied to W/raw/ with its metadata.
  python tools/sound/sound.py analyze <file>... [--spectro DIR]
        Duration, loudness (integrated, max momentary), true peak, silence at the ends, noise floor.
  python tools/sound/sound.py make <recipe.json>
        Build finished sounds from layers (trim, fade, pitch, filters, reverb, delay) and normalize each to the
        loudness target of its category (tools/sound/loudness.json). See README.md for the recipe format.
  python tools/sound/sound.py reference
        Render every event of the built banks, measure them, write the per-category targets (loudness.json).
  python tools/sound/sound.py check <event:/path>... [--banks DIR]
        Render events from the built banks and compare them with their category's target.
  python tools/sound/sound.py credit <recipe.json>
        Record every outside source the recipe's sounds use in FMODProject/credits.json (what it became, how it was
        changed), keep the recipe and its sources in tools/sound/recipes/, rewrite CREDITS.md.
  python tools/sound/sound.py credits
        Rewrite FMODProject/CREDITS.md from FMODProject/credits.json.
  python tools/sound/sound.py fmod <spec.json>
        Create or replace events (one take or several played at random, pitch / volume randomization, bus, bank)
        with FMOD Studio's command line, then build every bank and check them.

Work folders (W) live in tools/sound/work/<subject>/ (git-ignored): downloads and drafts. Finished sounds go to
FMODProject/Corrupted/Assets/<folder>/, credited in FMODProject/credits.json in the same commit.
"""
import argparse
import datetime
import html
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import tempfile
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
FMOD_ASSETS = os.path.join(ROOT, "FMODProject", "Corrupted", "Assets")
CREDITS_JSON = os.path.join(ROOT, "FMODProject", "credits.json")
CREDITS_MD = os.path.join(ROOT, "FMODProject", "CREDITS.md")
LOUDNESS = os.path.join(HERE, "loudness.json")
sys.path.insert(0, os.path.join(ROOT, "tools", "fmod"))

UA = {"User-Agent": "Mozilla/5.0 (CorruptionDuPortail sound-maker)"}
LICENSES = {  # Freesound filter name -> (short name, license URL). NonCommercial never: the game is sold on Steam.
    "cc0": ("Creative Commons 0", "CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/"),
    "by": ("Attribution", "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/"),
}


# ---------------------------------------------------------------- measuring

def ffmpeg(*args, capture=True):
    return subprocess.run(["ffmpeg", "-hide_banner", "-nostats", *args], capture_output=capture, text=True,
                          errors="replace")


def measure(path):
    """Loudness of a file. max_m = loudest 400 ms (LUFS), the measure for one-shots; integrated needs >= 0.4 s."""
    # Padded with silence: the 400 ms window of the momentary loudness needs it to measure a click shorter than that.
    r = ffmpeg("-i", path, "-af", "apad=pad_dur=0.5,ebur128=peak=true:framelog=info", "-f", "null", "-")
    s = r.stderr
    ms = [float(x) for x in re.findall(r"\bM:\s*(-?[\d.]+)", s)]
    integ = re.findall(r"I:\s+(-?[\d.]+) LUFS", s)
    peak = re.findall(r"Peak:\s+(-?[\d.]+|-inf) dBFS", s)
    st = ffmpeg("-i", path, "-af", "astats=metadata=0:measure_overall=RMS_level+Noise_floor+Peak_level"
                ":measure_perchannel=none", "-f", "null", "-").stderr
    noise = re.findall(r"Noise floor dB:\s*(-?[\d.]+|-inf)", st)
    dur = probe_duration(path)
    lead, tail = silence_ends(path, dur)
    out = {"file": path, "seconds": round(dur, 3),
           "max_m": max(ms) if ms else None,
           "integrated": float(integ[-1]) if integ and dur >= 0.4 else None,
           "true_peak": float(peak[-1]) if peak and peak[-1] != "-inf" else None,
           "noise_floor": float(noise[-1]) if noise and noise[-1] != "-inf" else None,
           "lead_silence": lead, "tail_silence": tail}
    if out["max_m"] is not None and out["max_m"] < -69:
        out["max_m"] = None
    return out


def probe_duration(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                       capture_output=True, text=True)
    try:
        return float(r.stdout.strip())
    except ValueError:
        return 0.0


def silence_ends(path, dur, threshold="-60dB"):
    r = ffmpeg("-i", path, "-af", "silencedetect=n=%s:d=0.02" % threshold, "-f", "null", "-").stderr
    starts = [float(x) for x in re.findall(r"silence_start: (-?[\d.]+)", r)]
    ends = [float(x) for x in re.findall(r"silence_end: (-?[\d.]+)", r)]
    lead = ends[0] if starts and abs(starts[0]) < 0.001 and ends else 0.0
    tail = dur - starts[-1] if starts and (len(ends) < len(starts)) else 0.0
    return round(lead, 3), round(max(tail, 0.0), 3)


def spectrogram(path, png):
    os.makedirs(os.path.dirname(os.path.abspath(png)), exist_ok=True)
    ffmpeg("-y", "-i", path, "-lavfi", "showspectrumpic=s=800x240:legend=0:color=intensity:scale=log", png)


# ---------------------------------------------------------------- searching

def secret(name):
    try:
        r = subprocess.run(["pwsh", "-NoLogo", "-File", os.path.expanduser("~/.claude/tools/secret.ps1"), name],
                           capture_output=True, text=True, timeout=60)
        return r.stdout.strip() if r.returncode == 0 and r.stdout.strip() else None
    except (OSError, subprocess.TimeoutExpired):
        return None


def http(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return r.read()


def search_api(key, words, licenses, dmin, dmax, n):
    lic = " OR ".join('"%s"' % LICENSES[l][0] for l in licenses)
    params = {"query": words, "filter": "license:(%s) duration:[%s TO %s]" % (lic, dmin, dmax),
              "fields": "id,name,username,license,duration,avg_rating,num_ratings,num_downloads,tags,url,previews",
              "page_size": min(n, 150), "sort": "score", "token": key}
    data = json.loads(http("https://freesound.org/apiv2/search/text/?" + urllib.parse.urlencode(params)))
    out = []
    for s in data.get("results", []):
        short = next((LICENSES[l][1] for l in LICENSES if LICENSES[l][2].split("/")[-3] in s["license"]), s["license"])
        out.append({"id": s["id"], "title": s["name"], "author": s["username"], "license": short,
                    "duration": round(s["duration"], 2), "rating": s.get("avg_rating"),
                    "downloads": s.get("num_downloads"), "tags": s.get("tags", [])[:8], "url": s["url"],
                    "preview": s["previews"]["preview-hq-ogg"]})
    return out


def search_html(words, licenses, dmin, dmax, n):
    """The public search page, one license at a time (so each result's license is known)."""
    out = []
    for l in licenses:
        q = {"q": words, "f": 'license:"%s"' % LICENSES[l][0], "d0": dmin, "d1": dmax}
        page = http("https://freesound.org/search/?" + urllib.parse.urlencode(q)).decode("utf-8", "replace")
        for block in page.split('class="bw-search__result"')[1:]:
            def attr(a):
                m = re.search(r'%s="([^"]*)"' % a, block)
                return html.unescape(m.group(1)) if m else None
            sid = attr("data-sound-id")
            if not sid:
                continue
            rating = re.search(r'aria-label="Average rating of ([\d.]+)"', block)
            out.append({"id": int(sid), "title": attr("data-title"), "author": attr("data-username"),
                        "license": LICENSES[l][1], "duration": round(float(attr("data-duration") or 0), 2),
                        "rating": float(rating.group(1)) if rating else None,
                        "downloads": int(attr("data-num-downloads") or 0), "tags": [],
                        "url": "https://freesound.org/people/%s/sounds/%s/" % (attr("data-username"), sid),
                        "preview": (attr("data-ogg") or "").replace("-lq.ogg", "-hq.ogg")})
    out.sort(key=lambda s: -(s["downloads"] or 0))
    return out[:n]


def cmd_search(a):
    licenses = [l.strip() for l in a.license.split(",") if l.strip()]
    for l in licenses:
        if l not in LICENSES:
            raise SystemExit("license must be cc0 or by (NonCommercial is never allowed: the game is sold)")
    key = os.environ.get("FREESOUND_API_KEY") or secret("Freesound")
    try:
        found = search_api(key, a.words, licenses, a.min_dur, a.max_dur, a.n) if key else None
    except Exception as e:  # API down or key refused: the public pages still work
        print("API search failed (%s), using the public pages" % e, file=sys.stderr)
        found = None
    via = "api" if found is not None else "html"
    if found is None:
        found = search_html(a.words, licenses, a.min_dur, a.max_dur, a.n)
    os.makedirs(a.work, exist_ok=True)
    path = os.path.join(a.work, "candidates.json")
    old = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
    old[a.words] = {"via": via, "results": found}
    json.dump(old, open(path, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    for s in found:
        print("%7d  %-38s %-16s %-9s %5.2fs  ★%s  ⬇%s" % (s["id"], (s["title"] or "")[:38], (s["author"] or "")[:16],
                                                       s["license"], s["duration"], s["rating"], s["downloads"]))
    print("(%d via %s, saved in %s)" % (len(found), via, path))


# ---------------------------------------------------------------- downloading

def sources_path(work):
    return os.path.join(work, "sources.json")


def load_sources(work):
    p = sources_path(work)
    return json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {}


def save_sources(work, src):
    json.dump(src, open(sources_path(work), "w", encoding="utf-8"), indent=1, ensure_ascii=False)


def cmd_fetch(a):
    os.makedirs(os.path.join(a.work, "raw"), exist_ok=True)
    src = load_sources(a.work)
    cands = {}
    cp = os.path.join(a.work, "candidates.json")
    if os.path.exists(cp):
        for q in json.load(open(cp, encoding="utf-8")).values():
            for s in q["results"]:
                cands[str(s["id"])] = s
    for sid in a.ids:
        c = cands.get(str(sid))
        page_url = c["url"] if c else "https://freesound.org/s/%s/" % sid
        page = http(page_url).decode("utf-8", "replace")
        lic = re.search(r'license text" href="(https?://creativecommons\.org/[^"]+)"', page)
        lic_url = lic.group(1).replace("http://", "https://") if lic else None
        if not lic_url or "-nc" in lic_url or "sampling+" in lic_url:
            print("%s: skipped, license %s is not usable in a sold game" % (sid, lic_url), file=sys.stderr)
            continue
        short = "CC0 1.0" if "publicdomain/zero" in lic_url else "CC BY " + (re.search(r"/by/([\d.]+)/", lic_url)
                                                                            or [None, "?"])[1]
        title = re.search(r'<meta property="og:audio:title" content="([^"]*)"', page)
        author = re.search(r'<meta property="og:audio:artist" content="([^"]*)"', page)
        url = re.search(r'<meta property="og:url" content="([^"]*)"', page)
        tags = sorted(set(re.findall(r"/browse/tags/([^/\"?]+)", page)))
        preview = c["preview"] if c else None
        if not preview:
            m = re.search(r"https://cdn\.freesound\.org/previews/\d+/%s_\d+-lq\.mp3" % sid, page)
            preview = m.group(0).replace("-lq.mp3", "-hq.ogg") if m else None
        out = os.path.join(a.work, "raw", "fs%s.ogg" % sid)
        with open(out, "wb") as fh:
            fh.write(http(preview))
        src["fs%s" % sid] = {"file": os.path.relpath(out, a.work).replace("\\", "/"), "source": "Freesound",
                             "id": int(sid), "title": html.unescape(title.group(1)) if title else None,
                             "author": html.unescape(author.group(1)) if author else None,
                             "author_url": "https://freesound.org/people/%s/" % (author.group(1) if author else ""),
                             "license": short, "license_url": lic_url, "url": url.group(1) if url else page_url,
                             "tags": tags, "fetched": datetime.date.today().isoformat(),
                             "quality": "preview HQ (Ogg ~192 kb/s); the original needs a Freesound login"}
        print("fs%s  %s by %s (%s)" % (sid, src["fs%s" % sid]["title"], src["fs%s" % sid]["author"], short))
    save_sources(a.work, src)


def cmd_add(a):
    os.makedirs(os.path.join(a.work, "raw"), exist_ok=True)
    src = load_sources(a.work)
    key = re.sub(r"[^a-z0-9]+", "-", (a.key or os.path.splitext(os.path.basename(a.file))[0]).lower()).strip("-")
    dst = os.path.join(a.work, "raw", key + os.path.splitext(a.file)[1].lower())
    shutil.copyfile(a.file, dst)
    src[key] = {"file": os.path.relpath(dst, a.work).replace("\\", "/"), "source": a.source, "title": a.title,
                "author": a.author, "author_url": a.author_url, "license": a.license, "license_url": a.license_url,
                "url": a.url, "fetched": datetime.date.today().isoformat()}
    save_sources(a.work, src)
    print(key, "->", dst)


# ---------------------------------------------------------------- analysing

def cmd_analyze(a):
    rows = []
    for f in a.files:
        m = measure(f)
        if a.spectro:
            os.makedirs(a.spectro, exist_ok=True)
            m["spectrogram"] = os.path.join(a.spectro, os.path.splitext(os.path.basename(f))[0] + ".png")
            spectrogram(f, m["spectrogram"])
        rows.append(m)
    print(json.dumps(rows, indent=1, ensure_ascii=False))


# ---------------------------------------------------------------- loudness targets

CATEGORIES = [  # (category, regex on the event path); first match wins
    ("music", r"Music|Project_38"),
    ("ambience", r"Ambiant|WaitingforPower|AbattreLePortail"),
    ("power", r"^event:/Characters/"),  # a role's power: the loud moments
    ("announce", r"ChainingAnnoucement|^event:/Interface/Opening$"),  # game moments everyone hears
    ("table", r"VoteCandle|VoteClock|ChainLift|ChainSplat"),  # objects on the table: candle, clock, chains
    ("ui", r"^event:/Interface/"),  # clicks: menus, messages, cards, light buttons
]
# Not a reference: placeholders synthesized by Claude (T19), at arbitrary levels. Their target is set by hand.
PROVISIONAL = r"VoteCandle|VoteClock|ChainLift|ChainSplat"


def category_of(path):
    for cat, rx in CATEGORIES:
        if re.search(rx, path or ""):
            return cat
    return "ui"


def targets():
    return json.load(open(LOUDNESS, encoding="utf-8"))["targets"]


def cmd_reference(a):
    import banks
    f = banks.Fmod(a.banks)
    events = f.events()
    f.close()
    tmp = tempfile.mkdtemp(prefix="soundref_")
    rows = []
    for e in events:
        secs = 20.0 if not e["oneshot"] else e["length_ms"] / 1000.0 + 2.0
        wav = os.path.join(tmp, e["guid"] + ".wav")
        banks.render_one(a.banks, e["guid"], wav, secs)
        m = measure(wav)
        rows.append({"event": e["path"], "category": category_of(e["path"]), "max_m": m["max_m"],
                     "integrated": m["integrated"], "true_peak": m["true_peak"],
                     "provisional": bool(re.search(PROVISIONAL, e["path"] or ""))})
    cats = {}
    for cat, _ in CATEGORIES:
        ref = [r for r in rows if r["category"] == cat and not r["provisional"]]
        vals = [r["max_m"] for r in ref if r["max_m"] is not None]
        ints = [r["integrated"] for r in ref if r["integrated"] is not None]
        if vals:
            cats[cat] = {"max_m": round(statistics.median(vals), 1),
                         "spread": [round(min(vals), 1), round(max(vals), 1)],
                         "integrated": round(statistics.median(ints), 1) if ints else None, "events": len(vals)}
    old = json.load(open(LOUDNESS, encoding="utf-8")) if os.path.exists(LOUDNESS) else {}
    out = {"why": "Loudness of the sounds already in the game, per category, measured on the built banks as the game "
                  "mixes them (event + bus). New sounds are normalized to their category's target so nothing jumps "
                  "out. max_m = loudest 400 ms in LUFS (one-shots), integrated = whole-file LUFS (music, loops).",
           "measured": datetime.date.today().isoformat(), "reference": cats,
           "targets": old.get("targets") or {c: {"max_m": v["max_m"]} for c, v in cats.items()},
           "targets_why": old.get("targets_why", {}),
           "events": rows}
    json.dump(out, open(LOUDNESS, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print(json.dumps(cats, indent=1))


def recipe_targets():
    """{event path: target} for sounds whose recipe overrides their category's target (Poyo picked a level)."""
    out = {}
    folder = os.path.join(HERE, "recipes")
    for name in os.listdir(folder) if os.path.isdir(folder) else []:
        if name.endswith(".json") and not name.endswith((".sources.json", ".fmod.json")):
            for snd in json.load(open(os.path.join(folder, name), encoding="utf-8")).get("sounds", []):
                if snd.get("event") and "target_max_m" in snd:
                    out[snd["event"]] = snd["target_max_m"]
    return out


def cmd_check(a):
    import banks
    t = targets()
    own = recipe_targets()
    tmp = tempfile.mkdtemp(prefix="soundchk_")
    f = banks.Fmod(a.banks)
    events = {e["path"]: e for e in f.events()}
    f.close()
    bad = 0
    for path in a.events:
        e = events.get(path)
        if not e:
            print("%s: not in the banks" % path)
            bad += 1
            continue
        wav = os.path.join(tmp, e["guid"] + ".wav")
        banks.render_one(a.banks, e["guid"], wav, e["length_ms"] / 1000.0 + 2.0 if e["oneshot"] else 20.0)
        m = measure(wav)
        cat = category_of(path)
        goal = own.get(path, t[cat]["max_m"])
        off = None if m["max_m"] is None else round(m["max_m"] - goal, 1)
        ok = off is not None and abs(off) <= a.tolerance and (m["true_peak"] or -99) <= -0.5
        bad += 0 if ok else 1
        print("%s %-55s %s max_m %s (target %s, %+s LU) peak %s dBTP" % (
            "ok" if ok else "!!", path, cat, m["max_m"], goal, off, m["true_peak"]))
    return 1 if bad else 0


# ---------------------------------------------------------------- making

def layer_filter(i, L, sr=48000):
    """ffmpeg filter chain for one layer of a recipe."""
    f = []
    if L.get("reverse"):
        f.append("areverse")
    if "start" in L or "end" in L:
        f.append("atrim=start=%s%s" % (L.get("start", 0), ":end=%s" % L["end"] if "end" in L else ""))
        f.append("asetpts=PTS-STARTPTS")
    if L.get("pitch"):  # semitones, speed changes with it (like FMOD's pitch)
        r = 2 ** (L["pitch"] / 12.0)
        f.append("asetrate=%d*%s,aresample=%d" % (sr, r, sr))
    if L.get("highpass"):
        f.append("highpass=f=%s" % L["highpass"])
    if L.get("lowpass"):
        f.append("lowpass=f=%s" % L["lowpass"])
    for band in L.get("eq", []):  # [freq Hz, gain dB, width Q]
        f.append("equalizer=f=%s:t=q:w=%s:g=%s" % (band[0], band[2] if len(band) > 2 else 1, band[1]))
    if L.get("fade_in"):
        f.append("afade=t=in:d=%s" % L["fade_in"])
    if L.get("fade_out"):
        f.append("areverse,afade=t=in:d=%s,areverse" % L["fade_out"])
    if L.get("gain"):
        f.append("volume=%sdB" % L["gain"])
    if L.get("delay"):
        ms = int(float(L["delay"]) * 1000)
        f.append("adelay=%d|%d" % (ms, ms))
    return ",".join(f) or "anull"


def make_one(sound, recipe_dir, out_dir, t):
    inputs, chains, labels = [], [], []
    for i, L in enumerate(sound["layers"]):
        src = L["src"] if os.path.isabs(L["src"]) else os.path.join(recipe_dir, L["src"])
        inputs += ["-i", src]
        chains.append("[%d:a]aformat=sample_rates=48000:channel_layouts=stereo,%s[l%d]" % (i, layer_filter(i, L), i))
        labels.append("[l%d]" % i)
    mix = "".join(labels) + ("amix=inputs=%d:normalize=0:duration=longest" % len(labels) if len(labels) > 1
                             else "anull")
    post = []
    if sound.get("reverb"):  # small room: a few early reflections + a short tail
        rv = sound["reverb"]
        post.append("aecho=0.8:%s:%s:%s" % (rv.get("in", 0.9), rv.get("delays", "40|73|117"),
                                             rv.get("decays", "0.35|0.25|0.15")))
    if sound.get("mono"):
        post.append("pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0+0.5*c1")
    post.append("areverse,silenceremove=start_periods=1:start_threshold=-75dB,areverse")
    if sound.get("trim_lead", True):
        post.append("silenceremove=start_periods=1:start_threshold=-75dB")
    graph = ";".join(chains) + ";" + mix + ("," + ",".join(post) if post else "") + "[out]"
    tmp = os.path.join(tempfile.mkdtemp(prefix="soundmk_"), "pre.wav")
    r = subprocess.run(["ffmpeg", "-hide_banner", "-v", "error", "-y", *inputs, "-filter_complex", graph,
                        "-map", "[out]", "-ar", "48000", "-c:a", "pcm_f32le", tmp], capture_output=True, text=True)
    if r.returncode:
        raise SystemExit("ffmpeg failed for %s:\n%s" % (sound["name"], r.stderr))
    cat = sound["category"]
    goal = sound.get("target_max_m", t[cat]["max_m"])
    m = measure(tmp)
    gain = goal - m["max_m"]
    out = os.path.join(out_dir, sound["name"] + ".wav")
    os.makedirs(out_dir, exist_ok=True)
    subprocess.run(["ffmpeg", "-hide_banner", "-v", "error", "-y", "-i", tmp, "-af",
                    "volume=%.2fdB,alimiter=limit=0.89:level=false:attack=1:release=30" % gain,
                    "-ar", "48000", "-c:a", "pcm_s16le", out], check=True)
    res = measure(out)
    res.update({"name": sound["name"], "category": cat, "target_max_m": goal, "gain_applied": round(gain, 2)})
    return res


def cmd_make(a):
    recipe = json.load(open(a.recipe, encoding="utf-8"))
    rdir = os.path.dirname(os.path.abspath(a.recipe))
    out_dir = recipe["out"] if os.path.isabs(recipe["out"]) else os.path.join(ROOT, recipe["out"])
    t = targets()
    rows = [make_one(s, rdir, out_dir, t) for s in recipe["sounds"] if not a.only or s["name"] in a.only]
    for r in rows:
        print("%-28s max_m %6s (target %s) peak %s dBTP  %.2fs" % (r["name"], r["max_m"], r["target_max_m"],
                                                                  r["true_peak"], r["seconds"]))
    if a.spectro:
        for r in rows:
            spectrogram(r["file"], os.path.join(a.spectro, r["name"] + ".png"))


# ---------------------------------------------------------------- credits

def describe(L):
    """A layer's changes, in words, for the credits."""
    d = []
    if "start" in L or "end" in L:
        d.append("cut %s-%s s" % (L.get("start", 0), L.get("end", "end")))
    if L.get("reverse"):
        d.append("reversed")
    if L.get("pitch"):
        d.append("pitched %+g semitones" % L["pitch"])
    if L.get("highpass") or L.get("lowpass") or L.get("eq"):
        d.append("filtered")
    if L.get("fade_in") or L.get("fade_out"):
        d.append("faded")
    return ", ".join(d)


def cmd_credit(a):
    recipe = json.load(open(a.recipe, encoding="utf-8"))
    rdir = os.path.dirname(os.path.abspath(a.recipe))
    out_dir = recipe["out"] if os.path.isabs(recipe["out"]) else os.path.join(ROOT, recipe["out"])
    data = json.load(open(CREDITS_JSON, encoding="utf-8"))
    by_key = {(s["source"], str(s.get("id") or s["url"])): s for s in data["sounds"]}
    used_sources = {}
    for snd in recipe["sounds"]:
        wav = os.path.relpath(os.path.join(out_dir, snd["name"] + ".wav"), ROOT).replace("\\", "/")
        use = wav + (" (%s)" % snd["event"] if snd.get("event") else "")
        layers = snd["layers"]
        for L in layers:
            src = L["src"] if os.path.isabs(L["src"]) else os.path.join(rdir, L["src"])
            work = os.path.dirname(os.path.dirname(os.path.abspath(src)))  # <work>/raw/<file>
            key = os.path.splitext(os.path.basename(src))[0]
            meta = load_sources(work).get(key)
            if not meta:
                raise SystemExit("no source metadata for %s (fetch / add it first: every sound needs its license)" % src)
            used_sources[key] = meta
            k = (meta["source"], str(meta.get("id") or meta["url"]))
            entry = by_key.get(k)
            if not entry:
                entry = {k2: meta.get(k2) for k2 in ("source", "id", "title", "author", "author_url", "license",
                                                      "license_url", "url")}
                entry.update({"used_in": [], "changes": "", "added": datetime.date.today().isoformat()})
                data["sounds"].append(entry)
                by_key[k] = entry
            if use not in entry["used_in"]:
                entry["used_in"].append(use)
            change = describe(L)
            mixed = " mixed with other sounds," if len(layers) > 1 else ""
            note = "%s:%s %s, loudness normalized" % (snd["name"], mixed, change) if change else \
                "%s:%s loudness normalized" % (snd["name"], mixed)
            parts = [p for p in entry["changes"].split("; ") if p and not p.startswith(snd["name"] + ":")]
            entry["changes"] = "; ".join(parts + [note])
    json.dump(data, open(CREDITS_JSON, "w", encoding="utf-8", newline="\n"), indent=1, ensure_ascii=False)
    keep = os.path.join(HERE, "recipes")
    os.makedirs(keep, exist_ok=True)
    subject = os.path.splitext(os.path.basename(a.recipe))[0]
    if os.path.abspath(a.recipe) != os.path.abspath(os.path.join(keep, subject + ".json")):
        shutil.copyfile(a.recipe, os.path.join(keep, subject + ".json"))
    json.dump(used_sources, open(os.path.join(keep, subject + ".sources.json"), "w", encoding="utf-8"), indent=1,
              ensure_ascii=False)
    cmd_credits(a)


def cmd_credits(a):
    data = json.load(open(CREDITS_JSON, encoding="utf-8"))
    lines = ["# Sound credits", "",
             "Generated from `credits.json` by `tools/sound/sound.py credits`: edit the JSON, not this file. Every",
             "sound taken from outside the team is listed here with its license, what it became in the game and how",
             "it was changed. CC BY sounds must keep their line in the game's credits.", ""]
    by_license = {}
    for s in data["sounds"]:
        by_license.setdefault(s["license"], []).append(s)
    for lic in sorted(by_license):
        lines += ["## " + lic, ""]
        for s in sorted(by_license[lic], key=lambda s: (s["author"] or "", s["title"] or "")):
            who = "[%s](%s)" % (s["author"], s["author_url"]) if s.get("author_url") else s["author"]
            lines.append("- **[%s](%s)** by %s (%s, [license](%s))" % (s["title"], s["url"], who, s["source"],
                                                                       s["license_url"]))
            lines.append("  - used in: %s" % ", ".join("`%s`" % u for u in s["used_in"]))
            if s.get("changes"):
                lines.append("  - changes: %s" % s["changes"])
        lines.append("")
    attrib = sorted({"%s (%s)" % (s["author"], s["source"]) for s in data["sounds"] if s["license"].startswith("CC BY")})
    lines += ["## Line for the in-game credits", "",
              "Sounds: " + (", ".join(attrib) if attrib else "none to credit yet") + ".", ""]
    open(CREDITS_MD, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    print("wrote", CREDITS_MD, "(%d sounds)" % len(data["sounds"]))


# ---------------------------------------------------------------- FMOD

JS = r"""// Generated by tools/sound/sound.py fmod from %(spec)s.
var project = studio.project;
var spec = %(json)s;
function need(path) { var o = project.lookup(path); if (!o) { throw new Error("not found: " + path); } return o; }
function folderFor(path) {
    var parts = path.substring("event:/".length).split("/"); parts.pop();
    var cur = project.workspace.masterEventFolder, at = "event:";
    for (var i = 0; i < parts.length; i++) {
        at += "/" + parts[i];
        var f = project.lookup(at);
        if (!f) { f = project.create("EventFolder"); f.name = parts[i]; f.folder = cur; console.log("created " + at); }
        cur = f;
    }
    return cur;
}
function randomize(sound, prop, amount) {
    if (!amount) { return; }
    var m = project.create("RandomizerModulator");
    m.nameOfPropertyBeingModulated = prop;
    m.amount = amount;
    sound.relationships.modulators.add(m);
}
var bank = project.lookup("bank:/" + spec.bank);
if (!bank) { bank = project.create("Bank"); bank.name = spec.bank; console.log("created bank:/" + spec.bank); }
for (var i = 0; i < spec.events.length; i++) {
    var e = spec.events[i];
    var audios = [];
    for (var k = 0; k < e.files.length; k++) {
        var a = project.importAudioFile(spec.assets + e.files[k]);
        if (!a) { throw new Error("import failed: " + e.files[k]); }
        audios.push(a);
    }
    var length = 0;
    for (var k = 0; k < audios.length; k++) { length = Math.max(length, audios[k].length); }
    var event = project.lookup(e.path);
    var track;
    if (event) {
        // Replace the instruments of the event's first audio track, keep the rest (effects, buses, references).
        track = event.groupTracks[0];
        var old = track.modules.slice();
        for (var k = 0; k < old.length; k++) { project.deleteObject(old[k]); }
        console.log("replacing " + e.path + " " + event.id);
    } else {
        event = project.create("Event");
        event.name = e.path.substring(e.path.lastIndexOf("/") + 1);
        event.folder = folderFor(e.path);
        track = event.addGroupTrack("Audio");
        console.log("creating " + e.path);
    }
    var sound;
    if (audios.length === 1) {
        sound = track.addSound(event.timeline, "SingleSound", 0, length);
        sound.audioFile = audios[0];
    } else {
        sound = track.addSound(event.timeline, "MultiSound", 0, length);
        for (var k = 0; k < audios.length; k++) {
            var s = project.create("SingleSound");
            s.audioFile = audios[k];
            sound.relationships.sounds.add(s);
        }
    }
    sound.volume = e.volume || 0;
    randomize(sound, "pitch", e.pitch_random);
    randomize(sound, "volume", e.volume_random);
    if (e.bus) { event.mixerInput.output = need(e.bus); }
    var banks = event.relationships.banks.destinations;
    var has = false;
    for (var k = 0; k < banks.length; k++) { if (banks[k].id === bank.id) { has = true; } }
    if (!has) { event.relationships.banks.add(bank); }
    console.log("done " + e.path + " " + event.id + " (" + audios.length + " take(s), " + length + " s)");
}
project.save();
console.log("saved");
"""


def cmd_fmod(a):
    spec = json.load(open(a.spec, encoding="utf-8"))
    spec["assets"] = FMOD_ASSETS.replace("\\", "/") + "/"
    for e in spec["events"]:
        for f in e["files"]:
            if not os.path.exists(os.path.join(FMOD_ASSETS, f)):
                raise SystemExit("missing audio file: FMODProject/Corrupted/Assets/" + f)
    js = os.path.join(tempfile.mkdtemp(prefix="soundfmod_"), "apply.js")
    open(js, "w", encoding="utf-8").write(JS % {"spec": os.path.basename(a.spec), "json": json.dumps(spec)})
    sh = os.path.join(ROOT, "tools", "fmod", "fmod.sh")
    bash = shutil.which("bash") or r"C:\Program Files\Git\bin\bash.exe"
    subprocess.run([bash, sh, "script", js], check=True)
    if not a.no_build:
        subprocess.run([bash, sh, "build"], check=True)


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("search"); p.add_argument("words"); p.add_argument("--license", default="cc0,by")
    p.add_argument("--min-dur", type=float, default=0); p.add_argument("--max-dur", type=float, default=10)
    p.add_argument("-n", type=int, default=30); p.add_argument("--work", default=os.path.join(HERE, "work", "scratch"))
    p.set_defaults(fn=cmd_search)
    p = sub.add_parser("fetch"); p.add_argument("work"); p.add_argument("ids", nargs="+"); p.set_defaults(fn=cmd_fetch)
    p = sub.add_parser("add"); p.add_argument("work"); p.add_argument("file"); p.add_argument("--key")
    p.add_argument("--title", required=True); p.add_argument("--author", required=True)
    p.add_argument("--author-url"); p.add_argument("--license", required=True); p.add_argument("--license-url")
    p.add_argument("--url", required=True); p.add_argument("--source", default="other"); p.set_defaults(fn=cmd_add)
    p = sub.add_parser("analyze"); p.add_argument("files", nargs="+"); p.add_argument("--spectro")
    p.set_defaults(fn=cmd_analyze)
    p = sub.add_parser("make"); p.add_argument("recipe"); p.add_argument("--only", nargs="*")
    p.add_argument("--spectro"); p.set_defaults(fn=cmd_make)
    import banks
    p = sub.add_parser("reference"); p.add_argument("--banks", default=banks.BANKS); p.set_defaults(fn=cmd_reference)
    p = sub.add_parser("check"); p.add_argument("events", nargs="+"); p.add_argument("--banks", default=banks.BANKS)
    p.add_argument("--tolerance", type=float, default=1.5); p.set_defaults(fn=cmd_check)
    p = sub.add_parser("credit"); p.add_argument("recipe"); p.set_defaults(fn=cmd_credit)
    p = sub.add_parser("credits"); p.set_defaults(fn=cmd_credits)
    p = sub.add_parser("fmod"); p.add_argument("spec"); p.add_argument("--no-build", action="store_true")
    p.set_defaults(fn=cmd_fmod)
    a = ap.parse_args()
    sys.exit(a.fn(a) or 0)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
