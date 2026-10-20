#!/usr/bin/env python3
"""Check the deployed Firestore rules of the playtest logs, as a game client would hit them.

Signs up an anonymous account (like the game), tries what the game does and what it must not be able to do, prints
PASS/FAIL per case, then deletes its test documents with the developer's gcloud credentials.

    python tools/logs/check_rules.py
"""
import json
import subprocess
import sys
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

PROJECT = "corruption-du-portail"
API_KEY = "AIzaSyD72okoz-olxXghpFhNaT6XjiWHTVzzVxM"  # the game's Web API key (not a secret)
BASE = f"https://firestore.googleapis.com/v1/projects/{PROJECT}/databases/(default)/documents"


def call(method, url, body=None, token=None, admin=False):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    if admin:
        headers["x-goog-user-project"] = PROJECT
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body is not None else None, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()


def ts(dt):
    return {"timestampValue": dt.strftime("%Y-%m-%dT%H:%M:%SZ")}


def main():
    code, text = call("POST", f"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={API_KEY}", {"returnSecureToken": True})
    if code != 200:
        sys.exit(f"sign-up failed {code} {text[:300]}")
    auth = json.loads(text)
    tok, uid = auth["idToken"], auth["localId"]
    now = datetime.now(timezone.utc)
    sid = "rulescheck-" + now.strftime("%H%M%S")
    good_session = {"uid": {"stringValue": uid}, "sid": {"stringValue": sid}, "expireAt": ts(now + timedelta(days=60)),
                    "player": {"stringValue": "RulesCheck"}}
    mask = "?" + "&".join(f"updateMask.fieldPaths={k}" for k in good_session)

    def chunk(seq, **extra):
        f = {"sid": {"stringValue": sid}, "seq": {"integerValue": str(seq)}, "lines": {"stringValue": '{"t":1}\n'},
             "expireAt": ts(now + timedelta(days=60))}
        f.update(extra)
        return {"fields": f}

    cases = [
        ("config/logging readable", "GET", f"{BASE}/config/logging", None, 200),
        ("other config doc unreadable", "GET", f"{BASE}/config/other", None, 403),
        ("session create", "PATCH", f"{BASE}/sessions/{sid}{mask}", {"fields": good_session}, 200),
        ("session extra field refused", "PATCH", f"{BASE}/sessions/{sid}?updateMask.fieldPaths=evil",
         {"fields": {"evil": {"stringValue": "x"}}}, 403),
        ("session far expiry refused", "PATCH", f"{BASE}/sessions/{sid}?updateMask.fieldPaths=expireAt",
         {"fields": {"expireAt": ts(now + timedelta(days=400))}}, 403),
        ("session of another uid refused", "PATCH", f"{BASE}/sessions/{sid}-x{mask.replace(sid, sid)}",
         {"fields": dict(good_session, uid={"stringValue": "someone-else"}, sid={"stringValue": sid + "-x"})}, 403),
        ("chunk create", "POST", f"{BASE}/sessions/{sid}/chunks?documentId=000000", chunk(0), 200),
        ("same chunk again = ALREADY_EXISTS", "POST", f"{BASE}/sessions/{sid}/chunks?documentId=000000", chunk(0), 409),
        ("chunk seq/path mismatch refused", "POST", f"{BASE}/sessions/{sid}/chunks?documentId=000005", chunk(1), 403),
        ("chunk extra field refused", "POST", f"{BASE}/sessions/{sid}/chunks?documentId=000001",
         chunk(1, evil={"stringValue": "x"}), 403),
        ("chunk overwrite refused", "PATCH", f"{BASE}/sessions/{sid}/chunks/000000", chunk(0), 403),
        ("session read refused", "GET", f"{BASE}/sessions/{sid}", None, 403),
    ]
    failed = 0
    for name, method, url, body, expected in cases:
        code, text = call(method, url, body, tok)
        ok = code == expected
        failed += not ok
        print(f"{'PASS' if ok else 'FAIL'}  {name}: got {code}, expected {expected}" + ("" if ok else f"  {text[:200]}"))

    admin = subprocess.run("gcloud auth print-access-token", shell=True, capture_output=True, text=True).stdout.strip()
    for path in (f"sessions/{sid}/chunks/000000", f"sessions/{sid}"):
        call("DELETE", f"{BASE}/{path}", token=admin, admin=True)
    print("ALL PASS" if not failed else f"{failed} FAIL")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
