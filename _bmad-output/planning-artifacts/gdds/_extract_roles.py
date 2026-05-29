import os, re, glob, json

ROOT = r"C:\Users\azert\Documents\PROJECTS\Unity\CorruptionDuPortail\Assets"

FACTION = {0:"anomaly",1:"chosen",2:"marginal",3:"unknown"}
CTYPE = {1:"masked",2:"detective",3:"support",4:"tracker",5:"innocent"}

def read(p):
    with open(p, encoding="utf-8", errors="replace") as f:
        return f.read()

# ---- guid -> class name (from .cs.meta) ----
guid_class = {}
for meta in glob.glob(os.path.join(ROOT, "**", "*.cs.meta"), recursive=True):
    t = read(meta)
    m = re.search(r"guid:\s*([0-9a-f]+)", t)
    if m:
        guid_class[m.group(1)] = os.path.basename(meta)[:-8]  # strip .cs.meta

# ---- guid -> asset name (roles + power prefabs) ----
guid_asset = {}
for pat in ["ScriptableObjects/Characters/*.asset.meta", "Prefabs/Powers/*.prefab.meta", "ScriptableObjects/Powers/*.asset.meta"]:
    for meta in glob.glob(os.path.join(ROOT, pat)):
        t = read(meta)
        m = re.search(r"guid:\s*([0-9a-f]+)", t)
        if m:
            name = os.path.basename(meta).split(".")[0]
            guid_asset[m.group(1)] = name

def decode_fixedstring(block, field):
    # find 'field:' then utf8LengthInBytes then collect that many byte values in order
    i = block.find(field + ":")
    if i < 0: return ""
    sub = block[i:]
    m = re.search(r"utf8LengthInBytes:\s*(\d+)", sub)
    if not m: return ""
    n = int(m.group(1))
    after = sub[m.end():]
    vals = re.findall(r"byte\d+:\s*(\d+)", after)
    bs = bytes(int(x) for x in vals[:n])
    try:
        return bs.decode("utf-8", errors="replace")
    except: return ""

def split_blocks(text):
    parts = re.split(r"^--- !u!\d+ &\d+\s*$", text, flags=re.M)
    return parts

OUT = []

# ================= ROLES =================
OUT.append("===== ROLES =====")
roles = {}
for asset in sorted(glob.glob(os.path.join(ROOT, "ScriptableObjects/Characters/*.asset"))):
    name = os.path.basename(asset)[:-6]
    t = read(asset)
    rt = re.search(r"^\s{4}roleType:\s*(\d+)", t, re.M)
    ft = re.search(r"^\s{4}factionType:\s*(\d+)", t, re.M)
    rid = re.search(r"^\s{4}roleID:\s*(\d+)", t, re.M)
    rd = re.search(r"^\s{4}roleDifficulty:\s*(\d+)", t, re.M)
    wins = re.findall(r"type:\s*\{class:\s*(\w+)", t)
    # powers section
    powers = []
    pm = re.search(r"^\s*powers:\s*$(.*?)^\s*references:", t, re.M|re.S)
    block = pm.group(1) if pm else ""
    if not pm:
        pm2 = re.search(r"^\s*powers:\s*$(.*)", t, re.M|re.S)
        block = pm2.group(1) if pm2 else ""
    for g in re.findall(r"guid:\s*([0-9a-f]+)", block):
        powers.append(guid_asset.get(g, g[:8]))
    faction = FACTION.get(int(ft.group(1)), ft.group(1)) if ft else "?"
    ctype = CTYPE.get(int(rt.group(1)), rt.group(1)) if rt else "?"
    diff = rd.group(1) if rd else "?"
    rids = rid.group(1) if rid else "?"
    roles[name] = dict(faction=faction, ctype=ctype, diff=diff, rid=rids, wins=wins, powers=powers)
    OUT.append(f"{name} | faction={faction} | type={ctype} | diff={diff} | id={rids} | wins={wins} | powers={powers}")

# ================= AWAKENING ORDER =================
OUT.append("\n===== AWAKENING ORDER =====")
aw = read(os.path.join(ROOT, "ScriptableObjects/GameStates/AwakeningState.asset"))
am = re.search(r"awakeningOrder:(.*?)currentlyAwakenedCharacters:", aw, re.S)
if am:
    layers = re.split(r"-\s*awakeningCharacters:", am.group(1))
    li = 0
    for lay in layers:
        guids = re.findall(r"guid:\s*([0-9a-f]+)", lay)
        if not guids: continue
        names = [guid_asset.get(g, g[:8]) for g in guids]
        OUT.append(f"  Layer {li}: {names}")
        li += 1

# ================= POWERS =================
OUT.append("\n===== POWERS =====")
# identify Power.cs guid and PowerComponent guids by class name
power_guid = next((g for g,c in guid_class.items() if c=="Power"), None)
for prefab in sorted(glob.glob(os.path.join(ROOT, "Prefabs/Powers/*.prefab"))):
    fname = os.path.basename(prefab)[:-7]
    t = read(prefab)
    blocks = split_blocks(t)
    PC_SET = {"PCChainer","PCConcentrated","PCPowerUnlockWhenChain","PCReparentOnChain"}
    pname=""; pscript=""; fields={}; comps=[]
    for b in blocks:
        sm = re.search(r"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]+)", b)
        if not sm: continue
        cls = guid_class.get(sm.group(1), sm.group(1)[:8])
        if "maxWaitTime:" in b:
            pscript = cls
            pname = decode_fixedstring(b, "powerName")
            for f in ["maxWaitTime","isPassive","hasToBeAwakened","targetIncludeFlags","maxPowerUse","powerUseRegenPerAwakening"]:
                fm = re.search(rf"^\s*{f}:\s*(-?\d+)", b, re.M)
                fields[f] = fm.group(1) if fm else "?"
        elif cls in PC_SET:
            mc = re.search(r"^\s*maxChain:\s*(\d+)", b, re.M)
            comps.append(cls + (f"(maxChain={mc.group(1)})" if mc else ""))
    OUT.append(f"{fname} | script={pscript} | name='{pname}' | passive={fields.get('isPassive')} | mustAwaken={fields.get('hasToBeAwakened')} | maxWait={fields.get('maxWaitTime')} | maxUse={fields.get('maxPowerUse')} | regen={fields.get('powerUseRegenPerAwakening')} | targetFlags={fields.get('targetIncludeFlags')} | comps={comps}")

print("\n".join(OUT))
