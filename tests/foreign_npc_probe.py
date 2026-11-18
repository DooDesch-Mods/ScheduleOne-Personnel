# Reads where Nightlife's twelve club NPCs stand in a running game and whether their body is drawn. Used by
# foreign_npc_check.ps1 (#104); also runs on its own against a slot that is already in a save:
#
#     python -I tests/foreign_npc_probe.py --slot mptest
#
# Sets noon (club closed), puts the player beside the holding point at the town hall, waits for culling to settle,
# then asks the bridge for each NPC's active renderers. An NPC counts as drawn when its skinned body or its impostor
# billboard is active. Prints one line per NPC, takes a screenshot and ends with "RESULT {json}".
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Workspace", "tools", "bridge"))
import s1  # noqa: E402

# GameObject names S1API gives the club NPCs (their first names), from Nightlife 1.3.0.
CLUB = ["Tank", "Viktor", "Nia", "Tyler", "Jade", "Marcus", "Sofia", "Eli", "Amara", "Ryan", "DJ", "Lena"]
STAND = (58.0, 1.0, 18.0)          # in front of the town hall, near the holding point
EYE = [46.0, 2.0, 27.0]            # west of the town hall steps, looking east along the holding point
LOOK = [55.0, 1.0, 26.5]

p = argparse.ArgumentParser()
p.add_argument("--slot", required=True)
p.add_argument("--settle", type=float, default=4.0)
a = p.parse_args()

game = s1.Game(a.slot)


def world(cmd, args, timeout=30000):
    answer = game.world(cmd, args, timeout_ms=timeout)
    if not answer.get("ok", True) and "result" not in answer:
        raise SystemExit(f"bridge {cmd} failed: {json.dumps(answer)[:400]}")
    return answer.get("result", answer)


def renderers(name, active_only):
    """Renderers below the NPC root @NPCs/<name>; "under" matches any ancestor name, so the path is checked too."""
    args = {"under": name, "kind": "renderer", "limit": 400}
    if active_only:
        args["includeInactive"] = False
    prefix = f"@Managers/@NPCs/{name}/"
    return [o for o in world("find_world_objects", args).get("objects", []) if o.get("path", "").startswith(prefix)]


world("set_time", {"time": 1200}, 8000)
world("teleport", {"x": STAND[0], "y": STAND[1], "z": STAND[2]}, 8000)
time.sleep(a.settle)

found = drawn = hidden = 0
rows = []
for name in CLUB:
    objs = renderers(name, active_only=True)
    if not objs and not renderers(name, active_only=False):
        print(f"{name}: not in the scene")
        continue
    found += 1
    body = [o for o in objs if o.get("type") == "SkinnedMeshRenderer"]
    impostor = [o for o in objs if o.get("name") == "Impostor"]
    pos = (body or impostor or objs or [{}])[0].get("bounds", {}).get("center")
    is_drawn = bool(body or impostor)
    if is_drawn:
        drawn += 1
    else:
        hidden += 1
    where = f"({pos[0]:.1f}, {pos[1]:.1f}, {pos[2]:.1f})" if pos else "?"
    print(f"{name}: {'DRAWN' if is_drawn else 'hidden'} at {where} - {len(body)} body renderer(s), impostor {'on' if impostor else 'off'}")
    rows.append({"name": name, "drawn": is_drawn, "pos": pos})

shot = world("camera_shot", {"eye": EYE, "target": LOOK, "sight": "off", "settleFrames": 10}, 20000)
world("camera_release", {}, 8000)
path = shot.get("path") or shot.get("shot") or json.dumps(shot)[:200]
print("RESULT " + json.dumps({"found": found, "drawnOutside": drawn, "hidden": hidden, "shot": path, "npcs": rows}))
