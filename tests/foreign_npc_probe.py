# Reads where the NPCs another mod adds stand in a running game and whether their body is drawn. Used by
# foreign_npc_check.ps1 (#104); also runs on its own against a slot that is already in a save:
#
#     python -I tests/foreign_npc_probe.py --slot mptest                 # Nightlife's twelve club NPCs
#     python -I tests/foreign_npc_probe.py --slot mptest --mod empire    # Empire 2.0's seventeen contacts
#
# Sets noon, puts the player in front of the town hall, waits for culling to settle, then asks the bridge for the
# renderers below @Managers/@NPCs and groups them per NPC (S1API names an NPC's GameObject after its first name).
# An NPC counts as drawn when its skinned body or its impostor billboard is active. Prints one line per NPC, takes a
# screenshot and ends with "RESULT {json}".
import argparse
import json
import math
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Workspace", "tools", "bridge"))
import s1  # noqa: E402

MODS = {
    # Nightlife 1.3.0: physical club NPCs that wait at a holding point beside the town hall.
    "nightlife": {
        "names": ["Tank", "Viktor", "Nia", "Tyler", "Jade", "Marcus", "Sofia", "Eli", "Amara", "Ryan", "DJ", "Lena"],
        "eye": [46.0, 2.0, 27.0], "look": [55.0, 1.0, 26.5],
    },
    # Empire 2.3.0: contact-only dealers and suppliers (IsPhysical false). "The" is both The Cousins and The Dealer.
    "empire": {
        "names": ["Combo", "Don Eladio", "Gustavo", "Hector", "The", "Jack", "Jesse", "Domingo", "Lydia", "Mike",
                  "Mr.", "Patch", "Saul", "Skinny", "Todd", "Tuco"],
        "eye": [46.0, 2.0, 27.0], "look": [55.0, 1.0, 26.5],
    },
}
STAND = (58.0, 1.0, 18.0)          # in front of the town hall

p = argparse.ArgumentParser()
p.add_argument("--slot", required=True)
p.add_argument("--mod", choices=sorted(MODS), default="nightlife")
p.add_argument("--settle", type=float, default=4.0)
a = p.parse_args()
preset = MODS[a.mod]

game = s1.Game(a.slot)


def world(cmd, args, timeout=30000):
    answer = game.world(cmd, args, timeout_ms=timeout)
    if not answer.get("ok", True) and "result" not in answer:
        raise SystemExit(f"bridge {cmd} failed: {json.dumps(answer)[:400]}")
    return answer.get("result", answer)


def personnel_positions():
    """Where Personnel's own physical NPCs stand, from 'personnel npcs'. Empty when Personnel is not installed.

    A pack NPC can share its first name (and so its GameObject name) with an NPC of the mod under test, e.g. a
    pack's "Jack" and Empire's Jack Welker; their renderers are told apart by position."""
    answer = game.run("personnel npcs", settle_ms=1500)
    lines = answer.get("result", answer).get("lines", [])
    spots = []
    for line in lines:
        text = line.get("text", "") if isinstance(line, dict) else str(line)
        m = re.search(r"\sat (-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\s*$", text)
        if m:
            spots.append(tuple(float(v) for v in m.groups()))
    print(f"Personnel's own physical NPCs: {len(spots)}" if lines else "Personnel not installed (no 'personnel npcs' output)")
    return spots


def owned_by_personnel(o, spots):
    pos = o.get("position")
    return bool(pos) and any(math.hypot(pos[0] - s[0], pos[2] - s[2]) < 1.5 for s in spots)


def npc_renderers(active_only, skip):
    """Renderers below @Managers/@NPCs, grouped by the NPC root's name, without those of Personnel's own NPCs."""
    args = {"under": "@NPCs", "kind": "renderer", "limit": 5000}
    if active_only:
        args["includeInactive"] = False
    groups = {}
    for o in world("find_world_objects", args, 60000).get("objects", []):
        parts = o.get("path", "").split("/")
        if len(parts) > 2 and parts[0] == "@Managers" and parts[1] == "@NPCs" and not owned_by_personnel(o, skip):
            groups.setdefault(parts[2], []).append(o)
    return groups


world("set_time", {"time": 1200}, 8000)
world("teleport", {"x": STAND[0], "y": STAND[1], "z": STAND[2]}, 8000)
time.sleep(a.settle)

own = personnel_positions()
active = npc_renderers(active_only=True, skip=own)
present = npc_renderers(active_only=False, skip=own)
roots = sorted(present)
print(f"{len(roots)} NPC root(s) below @Managers/@NPCs")

found = drawn = hidden = 0
rows = []
first_drawn = None
for name in preset["names"]:
    if name not in present:
        print(f"{name}: no GameObject below @NPCs")
        rows.append({"name": name, "present": False, "drawn": False, "pos": None})
        continue
    found += 1
    objs = active.get(name, [])
    body = [o for o in objs if o.get("type") == "SkinnedMeshRenderer"]
    impostor = [o for o in objs if o.get("name") == "Impostor"]
    pos = (body or impostor or objs or present[name] or [{}])[0].get("bounds", {}).get("center")
    is_drawn = bool(body or impostor)
    if is_drawn:
        drawn += 1
        first_drawn = first_drawn or pos
    else:
        hidden += 1
    where = f"({pos[0]:.1f}, {pos[1]:.1f}, {pos[2]:.1f})" if pos else "?"
    print(f"{name}: {'DRAWN' if is_drawn else 'hidden'} at {where} - {len(body)} active body renderer(s), "
          f"impostor {'on' if impostor else 'off'}, {len(present[name])} renderer(s) in total")
    rows.append({"name": name, "present": True, "drawn": is_drawn, "pos": pos})

eye, look = preset["eye"], preset["look"]
if first_drawn and a.mod != "nightlife":
    # Look at the first drawn NPC from 6 m south-west, so the picture shows the body that was counted.
    look = [first_drawn[0], first_drawn[1], first_drawn[2]]
    eye = [first_drawn[0] - 4.0, first_drawn[1] + 1.5, first_drawn[2] - 4.5]
shot = world("camera_shot", {"eye": eye, "target": look, "sight": "off", "settleFrames": 10}, 20000)
world("camera_release", {}, 8000)
path = shot.get("path") or shot.get("shot") or json.dumps(shot)[:200]
print("RESULT " + json.dumps({"mod": a.mod, "expected": len(preset["names"]), "found": found, "drawnOutside": drawn,
                              "hidden": hidden, "shot": path, "npcs": rows}))
