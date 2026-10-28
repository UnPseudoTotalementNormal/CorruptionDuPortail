"""Sound maker: find free sound effects, shape them, level them to the project's own mix, keep the credits.

  python sound.py search "<words>" [--max-dur S] [--min-dur S] [--license cc0,by] [-n N] [--work W]
        Freesound (API with a key, else the public search pages). Writes W/candidates.json, prints the list.
  python sound.py fetch <W> <freesound id>...
        Download the HQ preview + the sound's page metadata (author, license) into W/raw/, W/sources.json.
  python sound.py add <W> <file> --title T --author A --license L --url U [--license-url LU] [--source S]
        A sound from another source (Kenney, Sonniss, OpenGameArt...): copied to W/raw/ with its metadata.
  python sound.py analyze <file>... [--spectro DIR]
        Duration, loudness (integrated, max momentary), true peak, silence at the ends, noise floor.
  python sound.py sheet <file>... --out sheet.png
        One picture: waveform + spectrogram of each file, with its name (to place cuts without hearing).
  python sound.py make <recipe.json> [--only NAME...] [--spectro DIR]
        Build finished sounds from layers (trim, fade, pitch, filters, delay) and normalize each to the loudness
        target of its category (loudness.json). Format: README.md.
  python sound.py reference [--files GLOB...]
        Measure the sounds already in the project (rendered by engine.py, or the files given), write the
        per-category loudness and targets in loudness.json.
  python sound.py check <name>... | check --files <file>...
        Measure sounds as the project plays them (engine.py) or files, compare with their target.
  python sound.py credit <recipe.json>
        Record every outside source the recipe uses in the credits file (what it became, how it was changed),
        keep the recipe and its sources in the recipes folder, rewrite the credits page.
  python sound.py credits
        Rewrite the credits page from the credits file.

Settings: sound.json next to this file (paths, project name, secret). Project-specific engine (render a sound as
the project plays it, put a finished sound into the engine): optional engine.py next to this file, with
sounds() -> [name], render(name, wav), and register(subparsers, sound_module) for its own commands.
Needs ffmpeg / ffprobe on PATH; Python standard library only (sheet needs Pillow).
"""
import argparse
import datetime
import glob
import html
import importlib.util
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


def find_root(path):
    """The repository holding this folder (first parent with .git), else the folder itself."""
    p = path
    while True:
        if os.path.exists(os.path.join(p, ".git")):
            return p
        up = os.path.dirname(p)
        if up == p:
            return path
        p = up


ROOT = find_root(HERE)
CONF = json.load(open(os.path.join(HERE, "sound.json"), encoding="utf-8"))


def rooted(p):
    return p if os.path.isabs(p) else os.path.join(ROOT, p)


CREDITS_JSON = rooted(CONF["credits"])
CREDITS_MD = rooted(CONF.get("credits_page", os.path.splitext(CONF["credits"])[0] + ".md"))
RECIPES = rooted(CONF.get("recipes", os.path.join(os.path.relpath(HERE, ROOT), "recipes")))
WORK = rooted(CONF.get("work", os.path.join(os.path.relpath(HERE, ROOT), "work")))
LOUDNESS = os.path.join(HERE, "loudness.json")

UA = {"User-Agent": "Mozilla/5.0 (%s sound-maker)" % CONF.get("project", "game")}
LICENSES = {  # Freesound filter name -> (Freesound name, short name, license URL). NonCommercial: never offered.
    "cc0": ("Creative Commons 0", "CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/"),
    "by": ("Attribution", "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/"),
}


def load_engine():
    p = os.path.join(HERE, "engine.py")
    if not os.path.exists(p):
        return None
    spec = importlib.util.spec_from_file_location("sound_engine", p)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


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
        r = subprocess.run(CONF["secret_command"] + [name],
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
            raise SystemExit("license must be cc0 or by (NonCommercial is never offered)")
    key = os.environ.get("FREESOUND_API_KEY") or (secret(CONF["secret_name"]) if CONF.get("secret_command")
                                                    else None)
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


def cmd_sheet(a):
    """Waveform + spectrogram per file, two columns, with the file name: read it to place the cuts."""
    from PIL import Image, ImageDraw
    tmp = tempfile.mkdtemp(prefix="soundsheet_")
    rows = []
    for i, f in enumerate(a.files):
        w, sp = os.path.join(tmp, "%d_w.png" % i), os.path.join(tmp, "%d_s.png" % i)
        ffmpeg("-y", "-i", f, "-filter_complex",
               "aformat=channel_layouts=mono,showwavespic=s=900x110:colors=white:scale=sqrt", "-frames:v", "1", w)
        ffmpeg("-y", "-i", f, "-lavfi", "showspectrumpic=s=900x110:legend=0:color=intensity:scale=log", sp)
        row = Image.new("RGB", (900, 250), (25, 25, 30))
        ImageDraw.Draw(row).text((6, 4), "%s  %.2fs" % (os.path.basename(f), probe_duration(f)), fill=(255, 220, 120))
        row.paste(Image.open(w).convert("RGB"), (0, 22))
        row.paste(Image.open(sp).convert("RGB").resize((900, 110)), (0, 136))
        rows.append(row)
    sheet = Image.new("RGB", (1800, 250 * ((len(rows) + 1) // 2)))
    for i, r in enumerate(rows):
        sheet.paste(r, ((i % 2) * 900, (i // 2) * 250))
    sheet.save(a.out)
    print(a.out)


# ---------------------------------------------------------------- loudness targets

def loudness_conf():
    return json.load(open(LOUDNESS, encoding="utf-8"))


def category_of(name, conf=None):
    """First category whose regex matches the sound's name (event path, file path...); the last one otherwise."""
    cats = (conf or loudness_conf())["categories"]
    for cat, rx in cats:
        if re.search(rx, name or ""):
            return cat
    return cats[-1][0]


def target_of(cat, targets):
    if cat not in targets:
        raise SystemExit("no loudness target for category %r: run `sound.py reference`, or set targets.%s.max_m in "
                         "loudness.json" % (cat, cat))
    return targets[cat]["max_m"]


def recipe_targets():
    """{sound name: target} for sounds whose recipe overrides their category's target (a level picked by ear)."""
    out = {}
    for p in glob.glob(os.path.join(RECIPES, "*.json")):
        if p.endswith((".sources.json", ".engine.json")):
            continue
        for snd in json.load(open(p, encoding="utf-8")).get("sounds", []):
            if snd.get("event") and "target_max_m" in snd:
                out[snd["event"]] = snd["target_max_m"]
    return out


def rendered(names, files):
    """[(name, wav)]: the files as they are, or the engine's render of each name."""
    if files:
        return [(f, f) for pat in names for f in (sorted(glob.glob(pat)) or [pat])]
    eng = load_engine()
    if not eng:
        raise SystemExit("no engine.py: give the sound files with --files")
    tmp = tempfile.mkdtemp(prefix="soundrender_")
    out = []
    for i, name in enumerate(names):
        wav = os.path.join(tmp, "%d.wav" % i)
        eng.render(name, wav)
        out.append((name, wav))
    return out


def cmd_reference(a):
    conf = loudness_conf()
    if a.files:
        items = rendered(a.files, True)
    else:
        eng = load_engine()
        if not eng:
            raise SystemExit("no engine.py: give the project's sound files with --files")
        items = rendered(eng.sounds(), False)
    exclude = conf.get("exclude")
    rows = []
    for name, wav in items:
        m = measure(wav)
        rows.append({"sound": name, "category": category_of(name, conf), "max_m": m["max_m"],
                     "integrated": m["integrated"], "true_peak": m["true_peak"],
                     "excluded": bool(exclude and re.search(exclude, name))})
    cats = {}
    for cat, _ in conf["categories"]:
        ref = [r for r in rows if r["category"] == cat and not r["excluded"]]
        vals = [r["max_m"] for r in ref if r["max_m"] is not None]
        ints = [r["integrated"] for r in ref if r["integrated"] is not None]
        if vals:
            cats[cat] = {"max_m": round(statistics.median(vals), 1),
                         "spread": [round(min(vals), 1), round(max(vals), 1)],
                         "integrated": round(statistics.median(ints), 1) if ints else None, "sounds": len(vals)}
    conf["measured"] = datetime.date.today().isoformat()
    conf["reference"] = cats
    for c, v in cats.items():
        conf.setdefault("targets", {}).setdefault(c, {"max_m": v["max_m"]})
    conf["sounds"] = rows
    json.dump(conf, open(LOUDNESS, "w", encoding="utf-8", newline="\n"), indent=1, ensure_ascii=False)
    print(json.dumps(cats, indent=1))


def cmd_check(a):
    conf = loudness_conf()
    own = recipe_targets()
    bad = 0
    for name, wav in rendered(a.names, a.files):
        m = measure(wav)
        cat = category_of(name, conf)
        goal = own[name] if name in own else target_of(cat, conf.get("targets", {}))
        off = None if m["max_m"] is None else round(m["max_m"] - goal, 1)
        ok = off is not None and abs(off) <= a.tolerance and (m["true_peak"] or -99) <= -0.5
        bad += 0 if ok else 1
        print("%s %-55s %s max_m %s (target %s, %+s LU) peak %s dBTP" % (
            "ok" if ok else "!!", name, cat, m["max_m"], goal, off, m["true_peak"]))
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
    if L.get("pitch"):  # semitones, speed changes with it (like an engine's pitch)
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
    goal = sound["target_max_m"] if "target_max_m" in sound else target_of(cat, t)
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
    t = loudness_conf()["targets"]
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


def load_credits():
    if os.path.exists(CREDITS_JSON):
        return json.load(open(CREDITS_JSON, encoding="utf-8"))
    return {"why": "Every sound taken from outside the project: source, author, license, where it is used, how it was "
                   "changed. Kept by sound.py credit; the .md page is generated from it.", "sounds": []}


def cmd_credit(a):
    recipe = json.load(open(a.recipe, encoding="utf-8"))
    rdir = os.path.dirname(os.path.abspath(a.recipe))
    out_dir = recipe["out"] if os.path.isabs(recipe["out"]) else os.path.join(ROOT, recipe["out"])
    data = load_credits()
    os.makedirs(os.path.dirname(CREDITS_JSON), exist_ok=True)
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
    keep = RECIPES
    os.makedirs(keep, exist_ok=True)
    subject = os.path.splitext(os.path.basename(a.recipe))[0]
    if os.path.abspath(a.recipe) != os.path.abspath(os.path.join(keep, subject + ".json")):
        shutil.copyfile(a.recipe, os.path.join(keep, subject + ".json"))
    json.dump(used_sources, open(os.path.join(keep, subject + ".sources.json"), "w", encoding="utf-8"), indent=1,
              ensure_ascii=False)
    cmd_credits(a)


def cmd_credits(a):
    data = load_credits()
    lines = ["# Sound credits", "",
             "Generated from `%s` by `sound.py credits`: edit the JSON, not this file. Every" % os.path.basename(CREDITS_JSON),
             "sound taken from outside the project is listed here with its license, what it became and how",
             "it was changed. CC BY sounds must keep their line in the credits players see.", ""]
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
    lines += ["## Line for the credits players see", "",
              "Sounds: " + (", ".join(attrib) if attrib else "none to credit yet") + ".", ""]
    open(CREDITS_MD, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    print("wrote", CREDITS_MD, "(%d sounds)" % len(data["sounds"]))


# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("search"); p.add_argument("words"); p.add_argument("--license", default="cc0,by")
    p.add_argument("--min-dur", type=float, default=0); p.add_argument("--max-dur", type=float, default=10)
    p.add_argument("-n", type=int, default=30); p.add_argument("--work", default=os.path.join(WORK, "scratch"))
    p.set_defaults(fn=cmd_search)
    p = sub.add_parser("fetch"); p.add_argument("work"); p.add_argument("ids", nargs="+"); p.set_defaults(fn=cmd_fetch)
    p = sub.add_parser("add"); p.add_argument("work"); p.add_argument("file"); p.add_argument("--key")
    p.add_argument("--title", required=True); p.add_argument("--author", required=True)
    p.add_argument("--author-url"); p.add_argument("--license", required=True); p.add_argument("--license-url")
    p.add_argument("--url", required=True); p.add_argument("--source", default="other"); p.set_defaults(fn=cmd_add)
    p = sub.add_parser("analyze"); p.add_argument("files", nargs="+"); p.add_argument("--spectro")
    p.set_defaults(fn=cmd_analyze)
    p = sub.add_parser("sheet"); p.add_argument("files", nargs="+"); p.add_argument("--out", required=True)
    p.set_defaults(fn=cmd_sheet)
    p = sub.add_parser("make"); p.add_argument("recipe"); p.add_argument("--only", nargs="*")
    p.add_argument("--spectro"); p.set_defaults(fn=cmd_make)
    p = sub.add_parser("reference"); p.add_argument("--files", nargs="*"); p.set_defaults(fn=cmd_reference)
    p = sub.add_parser("check"); p.add_argument("names", nargs="+"); p.add_argument("--files", action="store_true")
    p.add_argument("--tolerance", type=float, default=1.5); p.set_defaults(fn=cmd_check)
    p = sub.add_parser("credit"); p.add_argument("recipe"); p.set_defaults(fn=cmd_credit)
    p = sub.add_parser("credits"); p.set_defaults(fn=cmd_credits)
    eng = load_engine()
    if eng and hasattr(eng, "register"):
        eng.register(sub, sys.modules[__name__])
    a = ap.parse_args()
    sys.exit(a.fn(a) or 0)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
