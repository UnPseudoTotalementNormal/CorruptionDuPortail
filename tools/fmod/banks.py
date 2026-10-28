"""What the built FMOD banks hold, read with the game's own FMOD runtime (no FMOD Studio, no Unity).

  python tools/fmod/banks.py list [--banks DIR]            every event / bus / snapshot of the banks, with its GUID
  python tools/fmod/banks.py diff [--banks DIR]            banks vs FMODProject, event by event (GUID + path): exit 1
                                                           on any difference
  python tools/fmod/banks.py render <event:/path|{guid}> <out.wav> [--seconds N] [--banks DIR]
                                                           play one event offline into a WAV (what the game would hear)
  python tools/fmod/banks.py recover <event:/path|{guid}> <out.wav> [--banks DIR]
                                                           what one event plays, as a plain audio file (from its
                                                           start, reverb tail kept): the source of an event to rebuild
  python tools/fmod/banks.py setguid <event:/path> <{guid}> give a project event back its original GUID (the game
                                                           references events by GUID); FMOD Studio closed
  python tools/fmod/banks.py compare <old banks DIR> <new banks DIR> [--events a,b]
                                                           render every event from both bank sets, compare length/loudness

Why: the committed Master.bank was rebuilt for months from a project that was never committed (menu music 2026-02-09
…), so the banks and FMODProject/ drifted apart. `diff` shows the drift, `compare` proves a rebuild loses nothing.
Uses Assets/Plugins/FMOD/platforms/win/lib/x86_64/fmodstudio.dll (the core API is inside it). Windows, 64-bit Python.
"""
import argparse
import ctypes as C
import json
import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DLL = os.path.join(ROOT, "Assets", "Plugins", "FMOD", "platforms", "win", "lib", "x86_64", "fmodstudio.dll")
BANKS = os.path.join(ROOT, "Assets", "FMODBanks", "Desktop")
PROJECT = os.path.join(ROOT, "FMODProject", "Corrupted")
HEADER_VERSION = 0x00020307  # Assets/Plugins/FMOD/src/fmod.cs VERSION.number

OUTPUT_NOSOUND_NRT, OUTPUT_WAVWRITER_NRT = 4, 5
STUDIO_INIT_SYNCHRONOUS_UPDATE = 0x00000004
STUDIO_INIT_LOAD_FROM_UPDATE = 0x00000010
INIT_STREAM_FROM_UPDATE = 0x00000001
LOAD_BANK_NORMAL = 0
PLAYBACK_STOPPED = 2
LOADING_LOADED = 3
BLOCK = 1024
WARMUP_BLOCKS = 47  # ~1 s
WARMUP_SECONDS = WARMUP_BLOCKS * BLOCK / 48000.0


class GUID(C.Structure):
    _fields_ = [("Data1", C.c_uint32), ("Data2", C.c_uint16), ("Data3", C.c_uint16), ("Data4", C.c_ubyte * 8)]

    def __str__(self):
        d = bytes(self.Data4)
        return "{%08x-%04x-%04x-%s-%s}" % (self.Data1, self.Data2, self.Data3, d[:2].hex(), d[2:].hex())


class Fmod:
    """One FMOD Studio system with every bank of a folder loaded. NRT output: renders as fast as the CPU allows."""

    def __init__(self, banks_dir, wav=None):
        os.add_dll_directory(os.path.dirname(DLL))
        self.lib = C.WinDLL(DLL)
        self.sys = C.c_void_p()
        self.ok(self.lib.FMOD_Studio_System_Create(C.byref(self.sys), HEADER_VERSION), "System_Create")
        core = C.c_void_p()
        self.ok(self.lib.FMOD_Studio_System_GetCoreSystem(self.sys, C.byref(core)), "GetCoreSystem")
        self.ok(self.lib.FMOD_System_SetOutput(core, OUTPUT_WAVWRITER_NRT if wav else OUTPUT_NOSOUND_NRT), "SetOutput")
        # Fixed mix block (default differs per output): one Update() = BLOCK samples at 48 kHz.
        self.ok(self.lib.FMOD_System_SetDSPBufferSize(core, BLOCK, 4), "SetDSPBufferSize")
        self.ok(self.lib.FMOD_System_SetSoftwareFormat(core, 48000, 3, 0), "SetSoftwareFormat")  # 48 kHz stereo
        extra = C.c_char_p(wav.encode("mbcs")) if wav else None
        # Everything (bank loads, sample loads, streams) happens inside Update(): renders are bit-exact repeatable.
        self.ok(self.lib.FMOD_Studio_System_Initialize(self.sys, 256,
                                                       STUDIO_INIT_SYNCHRONOUS_UPDATE | STUDIO_INIT_LOAD_FROM_UPDATE,
                                                       INIT_STREAM_FROM_UPDATE, extra),
                "Initialize")
        self.banks = []
        names = sorted(os.listdir(banks_dir), key=lambda n: (not n.startswith("Master"), n))
        for name in names:
            if not name.endswith(".bank"):
                continue
            bank = C.c_void_p()
            self.ok(self.lib.FMOD_Studio_System_LoadBankFile(self.sys, os.path.join(banks_dir, name).encode("mbcs"),
                                                             LOAD_BANK_NORMAL, C.byref(bank)), "LoadBankFile " + name)
            self.banks.append((name[:-5], bank))

    def ok(self, result, what):
        if result != 0:
            raise RuntimeError("FMOD %s failed: error %d" % (what, result))

    def path_of(self, getter, handle):
        buf = C.create_string_buffer(512)
        got = C.c_int()
        if getter(handle, buf, 512, C.byref(got)) != 0:
            return None
        return buf.value.decode("utf-8")

    def events(self):
        """[{path, guid, bank, length_ms, oneshot, is3d, stream}] of every event of every loaded bank."""
        out = []
        L = self.lib
        for bank_name, bank in self.banks:
            count = C.c_int()
            L.FMOD_Studio_Bank_GetEventCount(bank, C.byref(count))
            if not count.value:
                continue
            arr = (C.c_void_p * count.value)()
            got = C.c_int()
            L.FMOD_Studio_Bank_GetEventList(bank, arr, count.value, C.byref(got))
            for desc in arr[:got.value]:
                guid = GUID()
                L.FMOD_Studio_EventDescription_GetID(C.c_void_p(desc), C.byref(guid))
                length, oneshot, is3d, stream = C.c_int(), C.c_int(), C.c_int(), C.c_int()
                L.FMOD_Studio_EventDescription_GetLength(C.c_void_p(desc), C.byref(length))
                L.FMOD_Studio_EventDescription_IsOneshot(C.c_void_p(desc), C.byref(oneshot))
                L.FMOD_Studio_EventDescription_Is3D(C.c_void_p(desc), C.byref(is3d))
                L.FMOD_Studio_EventDescription_IsStream(C.c_void_p(desc), C.byref(stream))
                out.append({"path": self.path_of(L.FMOD_Studio_EventDescription_GetPath, C.c_void_p(desc)),
                            "guid": str(guid), "bank": bank_name, "length_ms": length.value,
                            "oneshot": bool(oneshot.value), "is3d": bool(is3d.value), "stream": bool(stream.value)})
        return sorted(out, key=lambda e: e["path"] or e["guid"])

    def strings(self):
        """{guid: path} of everything the strings bank names (events, buses, VCAs, snapshots, banks)."""
        out = {}
        L = self.lib
        for _, bank in self.banks:
            count = C.c_int()
            if L.FMOD_Studio_Bank_GetStringCount(bank, C.byref(count)) != 0:
                continue
            for i in range(count.value):
                guid = GUID()
                buf = C.create_string_buffer(512)
                got = C.c_int()
                if L.FMOD_Studio_Bank_GetStringInfo(bank, i, C.byref(guid), buf, 512, C.byref(got)) == 0:
                    out[str(guid)] = buf.value.decode("utf-8")
        return out

    def render(self, event, seconds, mute=()):
        """Start one event and pump the mixer until it stops (or `seconds` elapse). Output goes to the WAV file:
        WARMUP_SECONDS of silence, then the event exactly from its start (its own delays kept)."""
        L = self.lib
        for path in mute:
            bus = C.c_void_p()
            self.ok(L.FMOD_Studio_System_GetBus(self.sys, path.encode("utf-8"), C.byref(bus)), "GetBus " + path)
            self.ok(L.FMOD_Studio_Bus_SetMute(bus, 1), "Bus_SetMute")
        desc = C.c_void_p()
        if event.startswith("{"):
            guid = GUID()
            self.ok(L.FMOD_Studio_ParseID(event.encode(), C.byref(guid)), "ParseID")
            self.ok(L.FMOD_Studio_System_GetEventByID(self.sys, C.byref(guid), C.byref(desc)), "GetEventByID")
        else:
            self.ok(L.FMOD_Studio_System_GetEvent(self.sys, event.encode("utf-8"), C.byref(desc)), "GetEvent")
        L.FMOD_Studio_EventDescription_LoadSampleData(desc)
        inst = C.c_void_p()
        self.ok(L.FMOD_Studio_EventDescription_CreateInstance(desc, C.byref(inst)), "CreateInstance")
        steps = int(seconds * 48000 / BLOCK) + 1
        # Let the sample data finish loading before the event starts, or the start of the sound moves with the load
        # time. These updates are written to the WAV too: the render starts with WARMUP_SECONDS of silence.
        loading = C.c_int()
        for i in range(WARMUP_BLOCKS):
            L.FMOD_Studio_System_Update(self.sys)
            L.FMOD_Studio_EventDescription_GetSampleLoadingState(desc, C.byref(loading))
        if loading.value not in (LOADING_LOADED,):
            print("warning: sample data still loading after warm-up (state %d)" % loading.value, file=sys.stderr)
        self.ok(L.FMOD_Studio_EventInstance_Start(inst), "Start")
        state = C.c_int()
        for i in range(steps):
            L.FMOD_Studio_System_Update(self.sys)
            L.FMOD_Studio_EventInstance_GetPlaybackState(inst, C.byref(state))
            if state.value == PLAYBACK_STOPPED and i > 2:
                break
        L.FMOD_Studio_EventInstance_Release(inst)

    def close(self):
        self.lib.FMOD_Studio_System_Release(self.sys)


def project_events():
    """{guid: path} of the events of FMODProject (Metadata/Event/*.xml + the folder tree)."""
    meta = os.path.join(PROJECT, "Metadata")
    folders, roots = {}, set()
    for name in os.listdir(os.path.join(meta, "EventFolder")):
        obj = ET.parse(os.path.join(meta, "EventFolder", name)).getroot().find("object")
        if obj.get("class") == "MasterEventFolder":  # the root "Master" folder is not part of event paths
            roots.add(obj.get("id"))
            continue
        nm = obj.find("property[@name='name']/value")
        parent = obj.find("relationship[@name='folder']/destination")
        folders[obj.get("id")] = (nm.text if nm is not None else None, parent.text if parent is not None else None)

    def folder_path(fid):
        parts = []
        while fid and fid not in roots and fid in folders:
            name, fid = folders[fid]
            parts.append(name)
        return "/".join(reversed(parts))

    out = {}
    for name in os.listdir(os.path.join(meta, "Event")):
        obj = ET.parse(os.path.join(meta, "Event", name)).getroot().find("object[@class='Event']")
        folder = obj.find("relationship[@name='folder']/destination")
        prefix = folder_path(folder.text) if folder is not None else ""
        path = "event:/" + (prefix + "/" if prefix else "") + obj.find("property[@name='name']/value").text
        out[obj.get("id")] = path
    return out


def cmd_list(args):
    f = Fmod(args.banks)
    ev = f.events()
    names = f.strings()
    f.close()
    print(json.dumps({"events": ev, "strings": names}, indent=1, ensure_ascii=False))


def cmd_diff(args):
    f = Fmod(args.banks)
    built = {e["guid"]: e for e in f.events()}
    f.close()
    source = project_events()
    missing = [built[g] for g in built if g not in source]
    stale = [{"guid": g, "path": p} for g, p in source.items() if g not in built]
    moved = [{"guid": g, "bank": built[g]["path"], "project": source[g]} for g in built
             if g in source and built[g]["path"] != source[g]]
    print(json.dumps({"banks_only": missing, "project_only": stale, "renamed": moved,
                      "counts": {"banks": len(built), "project": len(source)}}, indent=1, ensure_ascii=False))
    # Any difference fails: banks built from a project that was not committed (banks_only / renamed), or project
    # changes never built (project_only). CI (.github/workflows/fmod-sync.yml) and the commit hook run this.
    return 1 if missing or moved or stale else 0


def loudness(wav):
    """(integrated LUFS, true peak dBTP, seconds) of a WAV, measured by ffmpeg."""
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", wav, "-af", "ebur128=peak=true", "-f", "null", "-"],
                       capture_output=True, text=True, errors="replace")
    s = r.stderr
    i = re.findall(r"I:\s+(-?[\d.]+|-inf) LUFS", s)
    p = re.findall(r"Peak:\s+(-?[\d.]+|-inf) dBFS", s)
    d = re.findall(r"time=(\d+):(\d+):([\d.]+)", s) or re.findall(r"Duration: (\d+):(\d+):([\d.]+)", s)
    secs = None
    if d:
        h, m, sec = d[-1]
        secs = int(h) * 3600 + int(m) * 60 + float(sec)
    return (float(i[-1]) if i and i[-1] != "-inf" else None,
            float(p[-1]) if p and p[-1] != "-inf" else None, secs)


def pcm(wav):
    """Interleaved 16-bit stereo 48 kHz samples of any audio file (ffmpeg)."""
    import array
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", wav, "-f", "s16le", "-ac", "2", "-ar", "48000", "-"],
                         capture_output=True, check=True).stdout
    return array.array("h", raw)


def mismatch_db(a_wav, b_wav):
    """Energy of (b - a) relative to a, in dB: below -20 the two renders are the same sound (codec noise aside)."""
    a, b = pcm(a_wav), pcm(b_wav)
    n = max(len(a), len(b))
    num = den = 0
    for i in range(n):
        x = a[i] if i < len(a) else 0
        y = b[i] if i < len(b) else 0
        num += (y - x) * (y - x)
        den += x * x
    if den == 0:
        return None if num == 0 else 0.0
    import math
    return round(10 * math.log10(max(num, 1) / den), 1)


def render_one(banks_dir, event, wav, seconds, mute=()):
    # One process per render: FMOD's WAV writer only finalizes its header when the system is released.
    code = ("import sys; sys.path.insert(0, %r); import banks; f = banks.Fmod(%r, %r); f.render(%r, %r, %r); f.close()"
            % (os.path.dirname(os.path.abspath(__file__)), banks_dir, wav, event, seconds, tuple(mute)))
    subprocess.run([sys.executable, "-c", code], check=True)


def cmd_render(args):
    render_one(args.banks, args.event, os.path.abspath(args.out), args.seconds,
               [m for m in (args.mute or "").split(",") if m])
    lufs, peak, secs = loudness(args.out)
    print(json.dumps({"wav": os.path.abspath(args.out), "lufs": lufs, "peak_dbtp": peak, "seconds": secs}))


def cmd_recover(args):
    f = Fmod(args.banks)
    ev = [e for e in f.events() if e["guid"] == args.event or e["path"] == args.event]
    f.close()
    if not ev:
        raise SystemExit("no such event in the banks: " + args.event)
    e = ev[0]
    if not e["oneshot"]:
        raise SystemExit("%s loops or sustains: rebuild it by hand from its source sample (render it to find the "
                         "loop points)" % e["path"])
    tmp = os.path.join(tempfile.mkdtemp(prefix="fmodrec_"), "render.wav")
    render_one(args.banks, e["guid"], tmp, e["length_ms"] / 1000.0 + 4.0)
    # Drop the warm-up and the mixer latency (measured: 4 blocks), keep the event's own leading silence, cut the
    # trailing silence once the reverb tail is gone.
    skip = WARMUP_BLOCKS * BLOCK + 4 * BLOCK
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", tmp, "-af",
                    "atrim=start_sample=%d,areverse,silenceremove=start_periods=1:start_threshold=-85dB,areverse"
                    % skip, "-c:a", "pcm_s16le", os.path.abspath(args.out)], check=True)
    lufs, peak, secs = loudness(args.out)
    print(json.dumps({"event": e["path"], "guid": e["guid"], "wav": os.path.abspath(args.out), "lufs": lufs,
                      "peak_dbtp": peak, "seconds": secs}, ensure_ascii=False))


def cmd_setguid(args):
    paths = {p: g for g, p in project_events().items()}
    old = paths.get(args.event)
    if not old:
        raise SystemExit("no such event in FMODProject: " + args.event)
    new = args.guid.lower()
    if not re.fullmatch(r"\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}", new):
        raise SystemExit("bad GUID: " + args.guid)
    meta = os.path.join(PROJECT, "Metadata")
    if os.path.exists(os.path.join(meta, "Event", new + ".xml")):
        raise SystemExit("an event already has " + new)
    # An event GUID appears in its own file only (folders, banks and buses do not list their events), plus any
    # event instrument pointing at it: rewrite every Metadata file that mentions it.
    for d, _, files in os.walk(meta):
        for name in files:
            fp = os.path.join(d, name)
            with open(fp, encoding="utf-8") as fh:
                text = fh.read()
            if old in text:
                with open(fp, "w", encoding="utf-8", newline="") as fh:
                    fh.write(text.replace(old, new))
    os.replace(os.path.join(meta, "Event", old + ".xml"), os.path.join(meta, "Event", new + ".xml"))
    print(json.dumps({"event": args.event, "from": old, "to": new}))


def cmd_compare(args):
    f = Fmod(args.old)
    old = {e["guid"]: e for e in f.events()}
    f.close()
    f = Fmod(args.new)
    new = {e["guid"]: e for e in f.events()}
    f.close()
    only = set(args.events.split(",")) if args.events else None
    tmp = tempfile.mkdtemp(prefix="fmodcmp_")
    rows, bad = [], 0
    for g, e in sorted(old.items(), key=lambda kv: kv[1]["path"] or ""):
        if only and e["path"] not in only and g not in only:
            continue
        row = {"path": e["path"], "guid": g}
        if g not in new:
            row["problem"] = "missing in new banks"
            bad += 1
            rows.append(row)
            continue
        secs = min(max(e["length_ms"], new[g]["length_ms"]) / 1000.0 + 2.0, 30.0)
        if e["length_ms"] == 0:
            secs = 10.0  # loops / sustained events: compare the first 10 s
        a, b = os.path.join(tmp, g + "_old.wav"), os.path.join(tmp, g + "_new.wav")
        render_one(args.old, g, a, secs)
        render_one(args.new, g, b, secs)
        la, lb = loudness(a), loudness(b)
        diff = mismatch_db(a, b)
        # Length alone is not a problem: an event rebuilt from a render carries its reverb tail in the audio.
        row.update({"old": {"lufs": la[0], "peak": la[1], "length_ms": e["length_ms"]},
                    "new": {"lufs": lb[0], "peak": lb[1], "length_ms": new[g]["length_ms"]}, "mismatch_db": diff})
        if (la[0] is None) != (lb[0] is None) or (la[0] is not None and abs(la[0] - lb[0]) > 1.0) or \
                (diff is not None and diff > args.tolerance):
            row["problem"] = "differs"
            bad += 1
        rows.append(row)
    added = [new[g]["path"] for g in new if g not in old]
    print(json.dumps({"events": rows, "added_in_new": added, "problems": bad, "renders": tmp}, indent=1,
                     ensure_ascii=False))
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("list"); p.add_argument("--banks", default=BANKS); p.set_defaults(fn=cmd_list)
    p = sub.add_parser("diff"); p.add_argument("--banks", default=BANKS); p.set_defaults(fn=cmd_diff)
    p = sub.add_parser("render"); p.add_argument("event"); p.add_argument("out")
    p.add_argument("--seconds", type=float, default=15.0); p.add_argument("--banks", default=BANKS)
    p.add_argument("--mute", help="buses to mute, comma separated (bus:/Sfx,bus:/Reverb)")
    p.set_defaults(fn=cmd_render)
    p = sub.add_parser("recover"); p.add_argument("event"); p.add_argument("out"); p.add_argument("--banks", default=BANKS)
    p.set_defaults(fn=cmd_recover)
    p = sub.add_parser("setguid"); p.add_argument("event"); p.add_argument("guid"); p.set_defaults(fn=cmd_setguid)
    p = sub.add_parser("compare"); p.add_argument("old"); p.add_argument("new"); p.add_argument("--events")
    p.add_argument("--tolerance", type=float, default=-20.0, help="max mismatch_db still counted as the same sound")
    p.set_defaults(fn=cmd_compare)
    args = ap.parse_args()
    sys.exit(args.fn(args) or 0)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
