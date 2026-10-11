"""Where the autoplay package's tools live (com.unpseudo.autoplay: its own repository, pinned in Packages/manifest.json).

Packages/com.unpseudo.autoplay/Tools~ when a local clone overrides the package (to work on the package), else the newest
Library/PackageCache/com.unpseudo.autoplay@*/Tools~ (read-only: never edit it). Same rule as autoplay-tool.sh.
"""
import glob
import os
import subprocess
import sys

PROJECT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"  # inherited by the package tools we launch


def _find_in(root):
    embedded = os.path.join(root, "Packages", "com.unpseudo.autoplay")
    if os.path.isfile(os.path.join(embedded, "package.json")):  # a local clone, not leftovers of the old embedded copy
        return os.path.join(embedded, "Tools~")
    cached = glob.glob(os.path.join(root, "Library", "PackageCache", "com.unpseudo.autoplay@*", "Tools~"))
    return max(cached, key=os.path.getmtime) if cached else None


def tools_dir():
    found = _find_in(PROJECT)
    if found:
        return found
    # A worktree without Library yet: borrow the main checkout's copy (same rule as autoplay-tool.sh).
    try:
        common = subprocess.run(["git", "-C", PROJECT, "rev-parse", "--path-format=absolute", "--git-common-dir"],
                                capture_output=True, text=True).stdout.strip()
        found = _find_in(os.path.dirname(common)) if common else None
    except OSError:
        found = None
    if found:
        print(f"autoplay tools borrowed from the main checkout ({found})", file=sys.stderr)
        return found
    sys.exit(f"autoplay package not resolved in {PROJECT}: open the project in Unity once "
             "(or run 'unity command package_resolve')")


def tool(name):
    return os.path.join(tools_dir(), name)
