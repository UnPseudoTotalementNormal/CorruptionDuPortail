# Keeps only the intended changes of a scene saved from a batchmode editor: opening a scene there recomputes layout-driven
# RectTransform sizes (and dirties prefab instances), and those lines must not be committed.
#
#   python merge_scene.py <old scene> <saved scene> [--keep id id ...]        lists what changed, or merges with --keep
#
# The result is written over <saved scene>: the old scene, plus the documents listed in --keep as saved, plus every
# document that is new, minus every document that is gone.
import re
import sys


def documents(path):
    with open(path, encoding="utf8", newline="") as f:
        text = f.read().replace(chr(13) + chr(10), chr(10))
    parts = re.split(r"(?m)^(?=--- !u!)", text)
    header = parts[0]
    docs = {}
    order = []
    for part in parts[1:]:
        doc_id = re.match(r"--- !u!\d+ &(-?\d+)", part).group(1)
        docs[doc_id] = part
        order.append(doc_id)
    return header, docs, order


def describe(doc):
    kind = re.match(r"--- !u!(\d+)", doc).group(1)
    name = re.search(r"m_Name: (.*)", doc)
    owner = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", doc)
    return f"type {kind}" + (f" name {name.group(1).strip()}" if name else "") + (f" go {owner.group(1)}" if owner else "")


def main():
    old_path, new_path = sys.argv[1], sys.argv[2]
    keep = set(sys.argv[sys.argv.index("--keep") + 1:]) if "--keep" in sys.argv else None
    header, old, old_order = documents(old_path)
    _, new, new_order = documents(new_path)
    changed = [i for i in old_order if i in new and old[i] != new[i]]
    added = [i for i in new_order if i not in old]
    removed = [i for i in old_order if i not in new]
    if keep is None:
        for i in changed:
            print("changed", i, describe(new[i]))
        for i in added:
            print("added", i, describe(new[i]))
        for i in removed:
            print("removed", i, describe(old[i]))
        return
    out = [header]
    for i in old_order:
        if i in removed:
            continue
        out.append(new[i] if i in keep else old[i])
    last = out.pop()
    if not last.endswith("\n"):
        last += "\n"
    out.append(last)
    for i in added:
        out.append(new[i])
    with open(new_path, "w", encoding="utf8", newline="") as f:
        f.write("".join(out))
    print(f"kept {len(keep)} changed, {len(added)} added, {len(removed)} removed; dropped {len(changed) - len(keep & set(changed))} changed")


main()
