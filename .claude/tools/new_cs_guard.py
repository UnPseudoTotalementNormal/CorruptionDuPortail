"""PreToolUse hook (Write): a NEW C# file under Assets/ must be created by Unity first.

A .cs written straight from disk can sit in the AssetDatabase (MonoScript, Game.dll) and still be missing from the
compilation's source list: CS0246 on its type, or 0 errors and its tests in no suite. Hit again on 2026-11-21
(RoleFanLayout.cs: three refresh / recompile attempts before recreating it with create_script), although CLAUDE.md
and project-context.md already said it. Writing over a file that exists is allowed, so the flow is:
`unity command --project-path <checkout> create_script --name X --path Assets/...`, then Write its content.

Self-test: python new_cs_guard.py --test
"""
import json
import os
import re
import sys

MESSAGE = (
    "New C# file under Assets/: create it through Unity first, then write its content over it.\n"
    "  UNITY_NO_BANNER=1 unity command --project-path \"$(pwd -W)\" create_script --name {name} --path {folder}\n"
    "A .cs written from disk can be left out of the compilation (CS0246 on its type, or its tests in no suite), "
    "see project-context.md « New .cs files ». No editor for this checkout? Start the headless one "
    "(tools/HEADLESS_UNITY.md)."
)


def verdict(tool_name, file_path, exists):
    """Return the block message, or None to let the call through."""
    if tool_name != "Write" or not file_path:
        return None
    norm = file_path.replace("\\", "/")
    if not norm.lower().endswith(".cs") or exists:
        return None
    match = re.search(r"(?:^|/)(Assets/.*)/([^/]+)\.cs$", norm)
    if not match:
        return None
    return MESSAGE.format(name=match.group(2), folder=match.group(1))


def self_test():
    cases = [
        (("Write", "C:/p/Assets/Scripts/UI/New.cs", False), True),
        (("Write", "C:\\p\\Assets\\Scripts\\Tests\\Editor\\NewTests.cs", False), True),
        (("Write", "C:/p/Assets/Scripts/UI/Existing.cs", True), False),
        (("Edit", "C:/p/Assets/Scripts/UI/New.cs", False), False),
        (("Write", "C:/p/tools/build/x.cs", False), False),
        (("Write", "C:/p/Assets/Scripts/UI/notes.md", False), False),
        (("Write", "C:/p/Packages/com.x/Runtime/New.cs", False), False),
    ]
    bad = [args for args, blocked in cases if (verdict(*args) is not None) != blocked]
    msg = verdict("Write", "C:/p/Assets/Scripts/UI/New.cs", False)
    if "--name New --path Assets/Scripts/UI" not in msg:
        bad.append("message")
    print("new_cs_guard self-test:", "PASS" if not bad else f"FAIL {bad}")
    return 0 if not bad else 1


def main():
    if "--test" in sys.argv:
        sys.exit(self_test())
    try:
        data = json.load(sys.stdin)
    except Exception:
        sys.exit(0)
    tool_input = data.get("tool_input") or {}
    path = tool_input.get("file_path") or ""
    message = verdict(data.get("tool_name"), path, bool(path) and os.path.exists(path))
    if message:
        print(message, file=sys.stderr)
        sys.exit(2)
    sys.exit(0)


if __name__ == "__main__":
    main()
