"""Retention for autoplay runs: drop the images of PASSED runs older than N days, keep everything else.

Screenshots and recording frames are ~99 % of a runs folder; the reports, journals, logs and state exports that an
investigation reads are ~1 %. A passed run's images are no longer looked at after a couple of days, a failed one's may
be: so only runs proven PASS lose their images, and only their images.

PASS = the run's verdict.json says "pass": true; without a verdict, every report.json inside (host, clients) has
outcome "Completed". No verdict and no report (killed, in progress, campaign/sweep folders) = kept untouched.

Scope: the AutoplayRuns folder of every checkout of the repository (main + each `git worktree`), so one call from any
checkout applies the retention everywhere.

Usage: python Tools~/prune_runs.py [--project DIR] [--days 2] [--dry-run] [--quiet] [roots ...]
"""
import argparse
import json
import os
import subprocess
import sys
import time

IMAGES = (".png", ".jpg", ".jpeg", ".gif")


def checkouts(project):
    try:
        out = subprocess.run(["git", "-C", project, "worktree", "list", "--porcelain"], capture_output=True, text=True,
                             encoding="utf-8", errors="replace", check=True).stdout
        return [line[len("worktree "):].strip() for line in out.splitlines() if line.startswith("worktree ")]
    except (OSError, subprocess.CalledProcessError):
        return [project]


def load(path):
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def passed(run):
    verdict = load(os.path.join(run, "verdict.json"))
    if isinstance(verdict, dict) and "pass" in verdict:
        return verdict["pass"] is True
    outcomes = []
    for base, dirs, files in os.walk(run):
        if base[len(run):].count(os.sep) >= 2:
            dirs[:] = []
        if "report.json" in files:
            outcomes.append((load(os.path.join(base, "report.json")) or {}).get("outcome"))
    return bool(outcomes) and all(o == "Completed" for o in outcomes)


def prune(run, dry):
    freed, count = 0, 0
    for base, _, files in os.walk(run):
        for name in files:
            if name.lower().endswith(IMAGES):
                path = os.path.join(base, name)
                try:
                    size = os.path.getsize(path)
                    if not dry:
                        os.remove(path)
                    freed, count = freed + size, count + 1
                except OSError:
                    pass
    return freed, count


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("roots", nargs="*", help="runs folders (default: AutoplayRuns of every checkout)")
    ap.add_argument("--project", default=os.getcwd())
    ap.add_argument("--days", type=float, default=float(os.environ.get("AUTOPLAY_KEEP_IMAGES_DAYS", 2)))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--quiet", action="store_true", help="one summary line only")
    a = ap.parse_args()

    roots = a.roots or [os.path.join(c, "AutoplayRuns") for c in checkouts(a.project)]
    limit = time.time() - a.days * 86400
    total, files, runs = 0, 0, 0
    for root in roots:
        if not os.path.isdir(root):
            continue
        for entry in sorted(os.scandir(root), key=lambda e: e.name):
            if not entry.is_dir(follow_symlinks=False) or entry.stat().st_mtime > limit or not passed(entry.path):
                continue
            freed, count = prune(entry.path, a.dry_run)
            if count:
                total, files, runs = total + freed, files + count, runs + 1
                if not a.quiet:
                    print(f"{freed / 1e6:8.1f} MB  {count:5} images  {entry.path}")
    verb = "would free" if a.dry_run else "freed"
    print(f"prune_runs: {verb} {total / 1e9:.2f} GB ({files} images, {runs} passed runs older than {a.days:g} days)")


if __name__ == "__main__":
    sys.exit(main())
