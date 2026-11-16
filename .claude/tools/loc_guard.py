#!/usr/bin/env python3
"""Loc guard - no player-facing text hard-coded outside the language system (T36).

Every text a player reads goes through `Loc` (French source + translation table): `Loc.Tr`, `label.SetLoc`,
`Loc.Token` for what the server sends. A plain string compiles, reads fine in French and stays French for an
English player. Rule: _bmad-output/project-context.md "Languages".

Hooks (stdin = the hook payload):
  PostToolUse  Edit | MultiEdit | Write   on a game .cs: the lines just written are checked and the findings are
                                          reported to the session at once (exit 2, the edit itself is not undone).
  PreToolUse   Bash | PowerShell          on a `git commit`: the added lines of the commit are checked and the
                                          commit is blocked (exit 2) while one is left.

What is flagged, in game code (Assets/Scripts minus Tests, Editor, Autoplay, Spike, Facepunch, Rendering, the
language system itself and the Demo*DataSource harnesses), on a line without `Loc.` / `SetLoc`:
  - a string literal with French in it (an accented letter, guillemets, an ellipsis);
  - a string literal with words handed straight to something a player reads (`.text =`, `SetText(`, `new Label(`,
    `.tooltip =`, a chat message, a lobby error, a tooltip title...).
Not flagged: comments, logs, asserts, exceptions, attributes ([Tooltip], [Header]...).
Known limit: a text without any accent kept in a constant and displayed through that constant ("Rejoindre" in a
`const`, then `label.text = TheConst`) is seen by neither rule: nothing but the habit of `SetLoc` catches it.

Escape hatches, on the line itself:
  // loc      the literal IS a game text, translated where it is displayed (a const, a serialized default):
              the coverage test then requires its entry in Assets/Resources/Localization/texts.json
  // loc-ok   the literal is not a text for players (say why)

Other uses:
  loc_guard.py --test          self-test
  loc_guard.py --scan [root]   list every finding of the working tree (audit)

Any failure of the hook itself lets the work through: a broken guard must never block.
"""
import io
import json
import os
import re
import subprocess
import sys

GAME_ROOT = "Assets/Scripts/"
EXCLUDED = re.compile(
    r"/(Tests?|Editor|Autoplay|Spike|Spikes|Facepunch|Rendering)/|/Domain/Localization/|/Scripts/Localization/|/Demo\w*DataSource\.cs$")

LITERAL = re.compile(r'\$?@?"((?:[^"\\]|\\.)*)"')
HANDLED = re.compile(r"\bLoc\.|\bSetLoc(Plural)?\s*\(|//\s*loc\b")
NOT_FOR_PLAYERS = re.compile(
    r"GameLog\.|Debug\.Log|\bAssert\.|\bthrow\s+new\b|Exception\(|\bnameof\(|"
    r"^\s*\[|\[(Tooltip|Header|CreateAssetMenu|MenuItem|ContextMenu|FormerlySerializedAs|InspectorName|Obsolete|Category|"
    r"Explicit|TextArea|AddComponentMenu|RequireComponent)\b|"
    r"Journal\.Record|\.Emit\(|\.Fact\(|RejectUse\(|LogInvalidOnce|ReportMissingOnce|TripOnce\(")
FRENCH = re.compile(r"[À-ÖØ-öø-ÿŒœ«»…]")
WORDS = re.compile(r"[A-Za-zÀ-ÖØ-öø-ÿ]{2,}")
PLACEHOLDER = re.compile(r"\{[^}]*\}|</?[a-zA-Z][^>]*>")
# A literal given to something a player reads.
SINK = re.compile(
    r"(?:\.text|\.tooltip|\btext|\bmessage)\s*=\s*\$?@?\"|"
    r"\b(?:SetText|SetTooltipTitle|SetTooltipDescription|AddMessageLocal|ReportError|ReportJoinFailure|DisplayError|"
    r"ShowNotification|DisconnectClient)\s*\([^\"]*\$?@?\"|"
    r"\bOnLobbyError\?\.Invoke\s*\(\s*\$?@?\"|"
    r"\bnew\s+(?:Label|Button|ChatLocal|ChatBroadcast|ChatSendServer|DiscoverChat|ChatMessage|LobbyBackendException)\s*\([^\"]*\$?@?\"|"
    r"\b(?:Reason|FailureMessage)\s*=\s*\$?@?\"")


def is_game_code(path):
    path = path.replace("\\", "/")
    at = path.find(GAME_ROOT)
    return at >= 0 and path.endswith(".cs") and not EXCLUDED.search(path[at - 1 if at else 0:] if at else "/" + path)


def has_words(literal):
    return bool(WORDS.search(PLACEHOLDER.sub("", literal)))


def without_comment(line):
    """The line up to its trailing // comment (a // inside a string literal is text)."""
    in_string = False
    i = 0
    while i < len(line):
        c = line[i]
        if in_string:
            if c == chr(92):
                i += 1
            elif c == '"':
                in_string = False
        elif c == '"':
            in_string = True
        elif c == "/" and line[i:i + 2] == "//":
            return line[:i]
        i += 1
    return line


def finding(line):
    """The reason a line is flagged, or None."""
    stripped = line.strip()
    if not stripped or stripped.startswith(("//", "/*", "*", "#")):
        return None
    if HANDLED.search(line) or NOT_FOR_PLAYERS.search(line):
        return None
    # A literal alone on its line continues a statement of the line above, mostly a long [Tooltip("..." + "...")].
    if stripped.startswith('"') and (stripped.endswith(")]") or stripped.endswith("+")):
        return None
    code = without_comment(stripped)
    literals = [m.group(1) for m in LITERAL.finditer(code)]
    worded = [text for text in literals if has_words(text)]
    if not worded:
        return None
    french = [text for text in worded if FRENCH.search(text)]
    if french:
        return 'French text outside Loc: "%s"' % french[0][:70]
    if SINK.search(code):
        return 'text shown to the player outside Loc: "%s"' % worded[0][:70]
    return None


def check_lines(path, numbered_lines):
    found = []
    if not is_game_code(path):
        return found
    for number, line in numbered_lines:
        reason = finding(line)
        if reason:
            found.append((path.replace("\\", "/"), number, reason))
    return found


ADVICE = """
Every text a player reads goes through the language system (_bmad-output/project-context.md, "Languages"):
  shown on this machine      Loc.Tr("Texte {0}", arg)  /  label.SetLoc("Texte {0}", arg)  /  Loc.Plural(n, "...", "...")
  sent by the server         Loc.Token("Texte {0}", pseudo, Loc.Token(roleName))   (each client reads its language)
  a const / serialized default that is displayed elsewhere through Loc: end the line with  // loc
Then add its English to Assets/Resources/Localization/texts.json (EditMode LocalizationCoverageTests lists the gaps).
A literal that no player ever reads: end the line with  // loc-ok  and why."""


def report(found, header):
    lines = [header] + ["  %s:%s  %s" % (path, number, reason) for path, number, reason in found[:25]]
    if len(found) > 25:
        lines.append("  ... and %d more" % (len(found) - 25))
    sys.stderr.write("\n".join(lines) + "\n" + ADVICE + "\n")


# ---------------------------------------------------------------------------------------------- hook modes

def after_edit(payload):
    tool_input = payload.get("tool_input") or {}
    path = tool_input.get("file_path") or ""
    if not is_game_code(path):
        return 0
    written = []
    if "content" in tool_input:
        written.append(tool_input.get("content") or "")
    if "new_string" in tool_input:
        written.append(tool_input.get("new_string") or "")
    for edit in tool_input.get("edits") or []:
        written.append(edit.get("new_string") or "")
    old = "\n".join([tool_input.get("old_string") or ""] + [e.get("old_string") or "" for e in tool_input.get("edits") or []])
    old_lines = set(line.strip() for line in old.splitlines())
    lines = [(0, line) for text in written for line in text.splitlines() if line.strip() not in old_lines]
    # Line numbers: look the flagged lines up in the file as it is now.
    found = check_lines(path, lines)
    if not found:
        return 0
    try:
        current = io.open(path, encoding="utf-8-sig", errors="replace").read().splitlines()
    except OSError:
        current = []
    located = []
    for file_path, _, reason in found:
        text = reason.split('"', 1)[1].rsplit('"', 1)[0] if '"' in reason else ""
        number = next((i for i, line in enumerate(current, 1) if text and text[:40] in line), "?")
        located.append((file_path, number, reason))
    report(located, "Loc guard: this edit writes player-facing text outside the language system.")
    return 2


COMMIT = re.compile(r"\bgit\b(\s+-C\s+\S+)?(\s+-c\s+\S+)*\s+commit\b")


def repo_of(command, cwd):
    match = re.search(r"\bgit\s+-C\s+(\"([^\"]+)\"|'([^']+)'|(\S+))", command)
    if not match:
        match = re.search(r"(?:^|[;&|]\s*)(?:cd|Set-Location)\s+(\"([^\"]+)\"|'([^']+)'|([^\s;&|]+))", command)
    repo = next((g for g in (match.groups()[1:] if match else ()) if g), cwd)
    drive = re.match(r"^/([a-zA-Z])/(.*)$", repo or "")
    return "%s:/%s" % (drive.group(1), drive.group(2)) if drive else repo


def before_commit(payload):
    command = str((payload.get("tool_input") or {}).get("command") or "")
    if not COMMIT.search(command) or re.search(r"--amend\b.*--no-edit|--dry-run", command):
        return 0
    repo = repo_of(command, str(payload.get("cwd") or ""))
    if not repo or not os.path.isdir(repo):
        return 0
    everything = re.search(r"\scommit\b.*\s(-a\b|--all\b|-[a-zA-Z]*a[a-zA-Z]*\s)", command)
    diff = subprocess.run(["git", "-C", repo, "diff", "HEAD" if everything else "--cached", "--unified=0", "--",
                           "Assets/Scripts/*.cs"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if diff.returncode != 0:
        return 0
    found = diff_findings(diff.stdout)
    if not found:
        return 0
    report(found, "Loc guard: this commit adds %d line(s) of player-facing text outside the language system." % len(found))
    return 2


def diff_findings(diff_text):
    found = []
    path, number = None, 0
    for line in diff_text.splitlines():
        if line.startswith("+++ "):
            path = line[6:] if line.startswith("+++ b/") else None
            continue
        hunk = re.match(r"^@@ -\S+ \+(\d+)", line)
        if hunk:
            number = int(hunk.group(1))
            continue
        if path and line.startswith("+"):
            found.extend(check_lines(path, [(number, line[1:])]))
            number += 1
    return found


def scan(root):
    found = []
    base = os.path.join(root, "Assets", "Scripts")
    for folder, _, names in os.walk(base):
        for name in names:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(folder, name)
            relative = os.path.relpath(path, root).replace("\\", "/")
            with io.open(path, encoding="utf-8-sig", errors="replace") as source:
                found.extend(check_lines(relative, list(enumerate(source.read().splitlines(), 1))))
    for path, number, reason in found:
        print("%s:%s  %s" % (path, number, reason))
    print("%d finding(s)" % len(found))
    return 1 if found else 0


# ---------------------------------------------------------------------------------------------- self-test

def self_test():
    game = "Assets/Scripts/UI/Foo.cs"
    flagged = [
        'label.text = "Rejoindre";',
        'votesText.text = $"Votes: {count}";',
        'var title = new Label("Options de développement");',
        'OnLobbyError?.Invoke("Le nom du lobby ne peut pas être vide");',
        'private const string Hover = "Cliquer pour passer à la suite";  // a comment that is no mark',
        'return "Élu";',
        'effects.Add(new ChatBroadcast("Target hit", ChatWindows.Server, audience));',
        '_response.Reason = "Game already started";',
        'button.tooltip = "Close";',
    ]
    clean = [
        'label.text = Loc.Tr("Rejoindre");',
        'votesText.SetLoc("Votes: {0}", count);',
        'private const string Hover = "Cliquer pour skip le vote"; // loc',
        'private const string Layer = "Élément"; // loc-ok: a layer name',
        '// label.text = "Rejoindre";',
        'GameLog.Info("ui", "ui.rolecard.open", "rôle", name);',
        'Debug.LogWarning("Pouvoir introuvable");',
        '[Tooltip("Durée de l\'animation, en secondes.")]',
        'throw new InvalidOperationException("Rôle inconnu");',
        'label.text = count.ToString();',
        'label.text = "{0}/{1}";',
        'label.text = "›";',
        '"matériau de la bordure de carte.")]',
        '"with its own texture (« Petit sourire »). Empty = none." +',
        'int active = _data.GetActivePresetIndex(); // -1 = pool hand-edited ("Personnalisée")',
        'var go = new GameObject("Banner");',
        'animator.SetTrigger("Emote");',
        'root.Q<Label>("role-name");',
    ]
    failures = []
    for line in flagged:
        if not finding(line):
            failures.append("should be flagged: " + line)
    for line in clean:
        if finding(line):
            failures.append("should be clean: %s  (%s)" % (line, finding(line)))
    for path, expected in [
        (game, True),
        ("C:\\repo\\Assets\\Scripts\\Board\\Card.cs", True),
        ("Assets/Scripts/Tests/Editor/FooTests.cs", False),
        ("Assets/Scripts/Editor/Tool.cs", False),
        ("Assets/Scripts/Autoplay/AutoplayDriver.cs", False),
        ("Assets/Scripts/Domain/Localization/Loc.cs", False),
        ("Assets/Scripts/Localization/LocalizationRuntime.cs", False),
        ("Assets/Scripts/UI/InfoTable/DemoInfoTableDataSource.cs", False),
        ("Assets/Scripts/Domain/KeyHints.cs", True),
        ("tools/logs/logs.py", False),
    ]:
        if is_game_code(path) != expected:
            failures.append("is_game_code(%s) should be %s" % (path, expected))
    diff = "\n".join([
        "+++ b/Assets/Scripts/UI/Foo.cs", "@@ -10,0 +11,2 @@", '+        label.text = "Quitter";', '+        label.text = Loc.Tr("Quitter");',
        "+++ b/Assets/Scripts/Tests/Editor/FooTests.cs", "@@ -1,0 +2,1 @@", '+        label.text = "Quitter";',
    ])
    hits = diff_findings(diff)
    if [(p, n) for p, n, _ in hits] != [("Assets/Scripts/UI/Foo.cs", 11)]:
        failures.append("diff findings: %r" % hits)
    for failure in failures:
        print("FAIL " + failure)
    print("loc_guard self-test: %s" % ("%d failure(s)" % len(failures) if failures else "ok"))
    return 1 if failures else 0


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        return self_test()
    if len(sys.argv) > 1 and sys.argv[1] == "--scan":
        return scan(sys.argv[2] if len(sys.argv) > 2 else os.getcwd())
    try:
        raw = sys.stdin.buffer.read().decode("utf-8-sig", errors="replace")
        if not raw.strip():
            return 0
        payload = json.loads(raw)
        if payload.get("tool_name") in ("Bash", "PowerShell"):
            return before_commit(payload) if payload.get("hook_event_name") != "PostToolUse" else 0
        return after_edit(payload)
    except Exception:  # a broken guard never blocks work
        return 0


if __name__ == "__main__":
    sys.exit(main())
