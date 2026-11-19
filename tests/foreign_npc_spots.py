# Walks a running game through several times of day and, at each named spot,
# lists every NPC whose body is drawn within a radius, marking the NPCs of the mod under test. Used for #104, where
# a player saw Empire's contacts under the freeway bridge and behind the gas mart; also runs on its own:
#
#     python -I tests/foreign_npc_spots.py --slot mptest --tag A --spot bridge:-17,1,22 --spot gasmart:2,1,2:-8,4,14
#     python -I tests/foreign_npc_spots.py --slot mptest --tag B --times 0800 1500 2000 0100
#
# A spot is name:x,y,z or name:x,y,z:ex,ey,ez with a camera position; without one the camera looks from 12 m south.
# The slot save (template "played") is on day 141, so no sleeping is needed to be past day 2. The game's own sleep
# (forcesleep) stops at the day summary screen and needs a click.
#
# For each time and spot: sets the time, puts the player at the spot, waits for culling and schedules to settle,
# reads the active renderers below @Managers/@NPCs and takes a screenshot of the spot. Renderers at the positions of
# Personnel's own NPCs are skipped (a pack NPC can share a first name with a foreign one). Prints one line per
# finding and ends with "RESULT {json}".
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

NAMES = {
    # Empire 2.3.0: contact-only dealers and suppliers. GameObject names are their first names.
    "empire": ["Combo", "Don Eladio", "Gustavo", "Hector", "The", "Jack", "Jesse", "Domingo", "Lydia", "Mike",
               "Mr.", "Patch", "Saul", "Skinny", "Todd", "Tuco"],
}

p = argparse.ArgumentParser()
p.add_argument("--slot", required=True)
p.add_argument("--mod", choices=sorted(NAMES), default="empire")
p.add_argument("--tag", required=True, help="run label used in shot names, e.g. A or B")
p.add_argument("--spot", action="append", required=True, help="name:x,y,z[:ex,ey,ez] (repeatable)")
p.add_argument("--radius", type=float, default=30.0)
p.add_argument("--times", nargs="+", default=["0800", "1500", "2000", "0100"])
p.add_argument("--settle", type=float, default=6.0)
p.add_argument("--out", required=True, help="folder for the screenshots")
a = p.parse_args()

game = s1.Game(a.slot)
foreign = set(NAMES[a.mod])
spots = []
for s in a.spot:
    name, xyz, *cam = s.split(":")
    at = tuple(float(v) for v in xyz.split(","))
    eye = [float(v) for v in cam[0].split(",")] if cam else [at[0], at[1] + 6.0, at[2] - 12.0]
    spots.append((name, at, eye))
os.makedirs(a.out, exist_ok=True)


def world(cmd, args, timeout=30000):
    answer = game.world(cmd, args, timeout_ms=timeout)
    if not answer.get("ok", True) and "result" not in answer:
        raise SystemExit(f"bridge {cmd} failed: {json.dumps(answer)[:400]}")
    return answer.get("result", answer)


def console(cmd, settle_ms=1500):
    answer = game.run(cmd, settle_ms=settle_ms)
    lines = answer.get("result", answer).get("lines", [])
    return [ln.get("text", "") if isinstance(ln, dict) else str(ln) for ln in lines]


def personnel_positions():
    spots_ = []
    for text in console("personnel npcs"):
        m = re.search(r"\sat (-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\s*$", text)
        if m:
            spots_.append(tuple(float(v) for v in m.groups()))
    return spots_


def drawn_npcs(skip):
    """Root name -> list of body centers of NPCs whose skinned body or impostor is active."""
    found = world("find_world_objects", {"under": "@NPCs", "kind": "renderer", "includeInactive": False,
                                         "limit": 8000}, 60000).get("objects", [])
    bodies = {}
    for o in found:
        parts = o.get("path", "").split("/")
        if not (len(parts) > 2 and parts[0] == "@Managers" and parts[1] == "@NPCs"):
            continue
        if not (o.get("type") == "SkinnedMeshRenderer" or o.get("name") == "Impostor"):
            continue
        pos = o.get("position") or o.get("bounds", {}).get("center")
        if not pos or any(math.hypot(pos[0] - s[0], pos[2] - s[2]) < 1.5 for s in skip):
            continue
        c = o.get("bounds", {}).get("center") or pos
        key = (parts[2], round(c[0]), round(c[2]))
        bodies.setdefault(key, c)
    out = {}
    for (name, _, _), c in bodies.items():
        out.setdefault(name, []).append(c)
    # one entry per body: merge renderer centers of the same NPC that lie within 2 m of each other
    merged = {}
    for name, cs in out.items():
        keep = []
        for c in cs:
            if not any(math.hypot(c[0] - k[0], c[2] - k[2]) < 2.0 for k in keep):
                keep.append(c)
        merged[name] = keep
    return merged


rows = []
own = personnel_positions()
print(f"Personnel's own physical NPCs: {len(own)}" if own else "no Personnel NPCs listed (Personnel absent or none physical)")
for t in a.times:
    world("set_time", {"time": int(t)}, 8000)
    for spot_name, (x, y, z), eye in spots:
        world("teleport", {"x": x, "y": y, "z": z}, 8000)
        time.sleep(a.settle)
        own = personnel_positions() or own
        drawn = drawn_npcs(own)
        near, empire_any = [], []
        for name, cs in drawn.items():
            for c in cs:
                d = math.hypot(c[0] - x, c[2] - z)
                if name in foreign:
                    empire_any.append({"name": name, "pos": [round(v, 1) for v in c], "dist": round(d, 1)})
                if d <= a.radius:
                    near.append({"name": name, "foreign": name in foreign, "pos": [round(v, 1) for v in c],
                                 "dist": round(d, 1)})
        total = sum(len(cs) for cs in drawn.values())
        shot_name = f"{a.tag}-{t}-{spot_name}.png"
        shot = world("camera_shot", {"eye": eye, "target": [x, y + 1.0, z], "sight": "off",
                                     "settleFrames": 10}, 20000)
        world("camera_release", {}, 8000)
        src = shot.get("path") or shot.get("shot")
        dst = os.path.join(a.out, shot_name)
        if src and os.path.exists(src):
            with open(src, "rb") as f_in, open(dst, "wb") as f_out:
                f_out.write(f_in.read())
        else:
            print(f"WARN no screenshot file for {shot_name}: {json.dumps(shot)[:200]}")
            dst = None
        fn = [n for n in near if n["foreign"]]
        print(f"{a.tag} {t} {spot_name}: {total} NPC bodies drawn on the map, {len(near)} within {a.radius:.0f} m, "
              f"{len(fn)} of them {a.mod}: {', '.join(n['name'] + ' ' + str(n['dist']) + 'm' for n in fn) or '-'}"
              f" | {a.mod} drawn anywhere: {', '.join(e['name'] + ' at ' + str(e['pos']) for e in empire_any) or 'none'}")
        print(f"    others near: {', '.join(n['name'] for n in near if not n['foreign']) or '-'}")
        rows.append({"time": t, "spot": spot_name, "near": near, "foreignAnywhere": empire_any,
                     "shot": dst})

print("RESULT " + json.dumps({"tag": a.tag, "mod": a.mod, "rows": rows}))
