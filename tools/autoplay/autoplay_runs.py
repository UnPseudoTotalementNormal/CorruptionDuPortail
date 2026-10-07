"""Shared helpers for the Corruption du Portail run analyzers: load a run (single build run or a net run with one
folder per process), its journal events, the process -> player id map, and the host's final roster."""
import glob
import json
import os
import re


class Process:
    def __init__(self, folder):
        self.folder = folder
        self.name = os.path.basename(folder.rstrip("/\\"))
        self.events = []
        path = os.path.join(folder, "events.ndjson")
        if os.path.exists(path):
            with open(path, encoding="utf-8-sig") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        try:
                            self.events.append(json.loads(line))
                        except ValueError:
                            pass
        self.is_host = "-host-" in self.name or not re.search(r"-client\d+-", self.name)
        self.player_id = 0 if self.is_host else None
        for e in self.events:
            if e.get("kind") == "connected":
                m = re.search(r"as client (\d+)", e.get("detail", ""))
                if m:
                    self.player_id = int(m.group(1))
        self.report = None
        rp = os.path.join(folder, "report.json")
        if os.path.exists(rp):
            with open(rp, encoding="utf-8-sig") as f:
                self.report = json.load(f)

    def of(self, kind):
        return [e for e in self.events if e.get("kind") == kind]


def load_run(run):
    """A net run folder (one sub-folder per process) or a single process folder."""
    if os.path.exists(os.path.join(run, "events.ndjson")):
        return [Process(run)]
    return [Process(d) for d in sorted(glob.glob(os.path.join(run, "*"))) if os.path.isdir(d)
            and os.path.exists(os.path.join(d, "events.ndjson"))]


def host_of(processes):
    return next((p for p in processes if p.is_host), None)


def roster(processes):
    """{player id: (role, faction)} from the host's final roster ("8 Le Messager faction=chosen chained=…")."""
    host = host_of(processes)
    result = {}
    for line in (host.report or {}).get("finalRoster", []) if host else []:
        m = re.match(r"(\d+) (.*?) faction=(\w+)", line)
        if m:
            result[int(m.group(1))] = (m.group(2), m.group(3))
    return result


def fields(detail):
    """'chat=4 from=101 token=ap:101:3 text=…' -> dict (text keeps everything after 'text=')."""
    out = {}
    text_at = detail.find(" text=")
    if text_at >= 0:
        out["text"] = detail[text_at + 6:]
        detail = detail[:text_at]
    for part in detail.split():
        if "=" in part:
            k, v = part.split("=", 1)
            out[k] = v
    return out
