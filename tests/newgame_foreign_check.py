# Starts a NEW game in a test slot and checks whether NPCs that other mods add are drawn in the world (#111, #104)
# and what the character creator shows (#99). The slot must already run a save (session.ps1 ... -Save 5).
#
#     python -I tests/newgame_foreign_check.py --slot mptest2 --label empire
#     python -I tests/newgame_foreign_check.py --slot mptest2 --label empire --names Combo,Jesse,Saul
#
# The new game is built the way SetupScreen.StartGame builds it: SaveGame_5 is cleared, StreamingAssets/DefaultSave
# copied in, Game.json gets a new seed and Metadata.json PlayTutorial=false (no intro, the creator opens on spawn).
# Then the bridge loads slot 5. While loading ends, a screenshot is taken every 10 s (the creator).
#
# Foreign NPCs come from Personnel's npcs.json (source "mod"), written once the registry holds still. Without
# Personnel installed the names come from --names. Every foreign NPC whose skinned body or impostor is active
# counts as drawn. The scan runs twice: where the new game put the player, and again with the player standing beside
# the map origin, where S1API parks contact-only NPCs hidden. Ends with "PASS"/"FAIL" per check and "RESULT {json}".
import argparse
import datetime
import json
import math
import os
import random
import shutil
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Workspace", "tools", "bridge"))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Workspace", "tools", "slots"))
import s1  # noqa: E402

STEAMIDS = {"mptest": "76561199320154780", "mptest2": "76561199320154781", "mptest3": "76561199320154782",
            "mptest4": "76561199320154783", "mptest5": "76561199320154784", "mptest6": "76561199320154785"}
SAVES = os.path.join(os.environ["USERPROFILE"], r"AppData\LocalLow\TVGS\Schedule I\Saves")
# Places the reporter of #111 named: S1API's hiding spot at the map origin (behind the gas mart), the parking lot
# under the overpass and the town hall.
SPOTS = {"origin": (0.0, 1.0, 0.0), "townhall": (58.0, 1.0, 18.0)}
GAME_VERSION = "0.4.7f12"
# Vanilla NPC GameObjects that share a first name with Empire's contacts: Jack Knight, and The King, who lives in
# the sewer next to the map origin. Their renderers cannot be told apart by name, so these names do not count.
VANILLA_CLASH = {"Jack", "The"}


def log(level, text):
    print("[newgame] %s: %s" % (level, text), flush=True)


def dt(now):
    return {"DataType": "DateTimeData", "DataVersion": 0, "GameVersion": GAME_VERSION, "Year": now.year,
            "Month": now.month, "Day": now.day, "Hour": now.hour, "Minute": now.minute, "Second": now.second}


def prepare(game, slot):
    folder = os.path.join(SAVES, STEAMIDS[slot], "SaveGame_5")
    default = os.path.join(game.root, "Schedule I_Data", "StreamingAssets", "DefaultSave")
    if not os.path.isdir(default):
        log("error", "no DefaultSave at %s" % default)
        sys.exit(1)
    if os.path.isdir(folder):
        shutil.rmtree(folder)
    shutil.copytree(default, folder)
    seed = random.randint(0, 2**31 - 2)
    with open(os.path.join(folder, "Game.json"), "w", encoding="utf-8") as f:
        json.dump({"DataType": "GameData", "DataVersion": 0, "GameVersion": GAME_VERSION,
                   "OrganisationName": "NewGameCheck", "Seed": seed,
                   "Settings": {"ConsoleEnabled": True, "UseRandomizedMixMaps": False}}, f, indent=4)
    now = datetime.datetime.now()
    with open(os.path.join(folder, "Metadata.json"), "w", encoding="utf-8") as f:
        json.dump({"DataType": "MetaData", "DataVersion": 0, "GameVersion": GAME_VERSION,
                   "CreationDate": dt(now), "LastPlayedDate": dt(now), "CreationVersion": GAME_VERSION,
                   "LastSaveVersion": GAME_VERSION, "PlayTutorial": False}, f, indent=4)
    log("info", "new save in %s, seed %d" % (folder, seed))


def to_menu(game):
    h = game.health()
    if h.get("scene") == "Menu" and not h.get("loading"):
        log("info", "already in the menu")
        return
    answer = game.call("return_to_menu", {})
    log("info", "return_to_menu: ok=%s" % answer.get("ok"))
    for _ in range(40):
        time.sleep(3)
        h = game.health()
        if h.get("scene") == "Menu" and not h.get("loading"):
            log("info", "menu reached")
            time.sleep(4)
            return
    log("error", "menu not reached: %s" % h)
    sys.exit(1)


def world(game, cmd, args, timeout=30000):
    answer = game.world(cmd, args, timeout_ms=timeout)
    if not answer.get("ok", True) and "result" not in answer:
        raise SystemExit("bridge %s failed: %s" % (cmd, json.dumps(answer)[:400]))
    return answer.get("result", answer)


def wait_loaded(game, seconds):
    start = time.monotonic()
    shots = []
    while time.monotonic() - start < seconds:
        time.sleep(10)
        h = game.health()
        if h.get("loading") or not h.get("inGame"):
            log("info", "t=%d loading (scene=%s)" % (time.monotonic() - start, h.get("scene")))
            continue
        try:
            shots.append(game.shot())
            log("info", "t=%d creator shot %s" % (time.monotonic() - start, shots[-1]))
        except Exception as e:  # a failed screenshot is reported and the poll goes on
            log("warn", "screenshot failed: %s" % e)
        if len(shots) >= 12:  # the intro cards and title run over a minute before the creator opens
            return shots
    log("error", "save not loaded within %d s" % seconds)
    sys.exit(1)


def foreign_names(game, since, fallback, wait_s=90):
    """First names of the NPCs other mods added, from Personnel's npcs.json written after `since`."""
    path = os.path.join(game.root, "UserData", "Personify", "World", "npcs.json")
    until = time.monotonic() + wait_s
    while time.monotonic() < until:
        if os.path.isfile(path) and os.path.getmtime(path) >= since:
            with open(path, encoding="utf-8") as f:
                cat = json.load(f)
            mods = [n for n in cat.get("npcs", []) if n.get("source") == "mod"]
            log("info", "npcs.json: %d NPCs, %d from other mods: %s" % (
                len(cat.get("npcs", [])), len(mods), ", ".join(n["name"] for n in mods)))
            others = {n["name"].split(" ")[0] for n in cat.get("npcs", []) if n.get("source") != "mod"}
            return sorted({n["name"].split(" ")[0] for n in mods} | set(fallback)), "catalog", others | VANILLA_CLASH
        time.sleep(3)
    log("warn" if fallback else "error", "npcs.json not written since the load - using --names (%d)" % len(fallback))
    return sorted(set(fallback)), "names", set(VANILLA_CLASH)


def renderers(game, active_only):
    args = {"under": "@NPCs", "kind": "renderer", "limit": 6000}
    if active_only:
        args["includeInactive"] = False
    groups = {}
    for o in world(game, "find_world_objects", args, 60000).get("objects", []):
        parts = o.get("path", "").split("/")
        if len(parts) > 2 and parts[0] == "@Managers" and parts[1] == "@NPCs":
            groups.setdefault(parts[2], []).append(o)
    return groups


def scan(game, names, phase):
    """Per foreign NPC: present below @NPCs, drawn (active skinned body or impostor), where."""
    active = renderers(game, True)
    present = renderers(game, False)
    rows, drawn = [], []
    for name in names:
        if name not in present:
            # S1API names the GameObject after the whole first name, which can be two words ("Don Eladio").
            longer = [r for r in present if r.startswith(name + " ")]
            if longer:
                name = longer[0]
        if name not in present:
            rows.append({"name": name, "present": False, "drawn": False, "pos": None})
            log("info", "%s - %s: no GameObject below @NPCs" % (phase, name))
            continue
        objs = active.get(name, [])
        body = [o for o in objs if o.get("type") == "SkinnedMeshRenderer"]
        impostor = [o for o in objs if o.get("name") == "Impostor"]
        pos = (body or impostor or present[name] or [{}])[0].get("bounds", {}).get("center")
        is_drawn = bool(body or impostor)
        near = min(SPOTS, key=lambda k: math.dist((pos[0], pos[2]), (SPOTS[k][0], SPOTS[k][2]))) if pos else None
        rows.append({"name": name, "present": True, "drawn": is_drawn, "pos": pos, "nearest": near, "phase": phase})
        if is_drawn:
            drawn.append(rows[-1])
        log("info", "%s - %s: %s at %s (nearest spot %s), %d active body, impostor %s" % (
            phase, name, "DRAWN" if is_drawn else "hidden", pos, near, len(body), "on" if impostor else "off"))
    return rows, drawn


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--slot", default="mptest2")
    p.add_argument("--label", required=True)
    p.add_argument("--names", default="", help="comma-separated first names of foreign NPCs (used without Personnel)")
    p.add_argument("--load-seconds", type=int, default=240)
    p.add_argument("--origin-wait", type=float, default=10.0, help="seconds beside the map origin before the scan")
    p.add_argument("--no-new-game", action="store_true", help="scan the save that is already loaded")
    a = p.parse_args()
    fallback = [n.strip() for n in a.names.split(",") if n.strip()]

    game = s1.Game(a.slot)
    if a.no_new_game:
        log("info", "--no-new-game: scanning the running save")
        creator_shots = []
        since = 0
    else:
        to_menu(game)
        prepare(game, a.slot)
        since = time.time()
        answer = game.call("load_save_by_slot", {"slot": 5}, timeout_ms=60000)
        if not answer.get("ok"):
            log("error", "load_save_by_slot failed: %s" % answer.get("error"))
            sys.exit(1)
        creator_shots = wait_loaded(game, a.load_seconds)
    names, origin_of_names, clash = foreign_names(game, since, fallback)
    skipped = sorted(n for n in names if n in clash)
    names = [n for n in names if n not in clash]
    if skipped:
        log("info", "not counted, a vanilla or Personnel NPC has the same GameObject name: %s" % ", ".join(skipped))

    world(game, "set_time", {"time": 1200}, 8000)
    time.sleep(6)  # past the 3 s after a level load in which the game forces every avatar visible
    rows_spawn, drawn_spawn = scan(game, names, "at spawn")
    # S1API parks contact-only NPCs hidden at the map origin. Walking up to it is what makes an LOD mod promote
    # them to its "full" tier, so the second scan stands the player next to it, looking at it.
    world(game, "teleport", {"x": 8.0, "y": 1.0, "z": 8.0}, 8000)
    time.sleep(a.origin_wait)
    rows, drawn = scan(game, names, "beside the origin")
    seen = {r["name"] for r in drawn}
    drawn += [r for r in drawn_spawn if r["name"] not in seen]

    shots = {}
    targets = [("origin", SPOTS["origin"])] + [(r["name"], tuple(r["pos"])) for r in drawn[:2]]
    for label, t in targets:
        shot = world(game, "camera_shot", {"eye": [t[0] - 5.0, t[1] + 2.0, t[2] - 5.0], "target": list(t),
                                           "sight": "off", "settleFrames": 10}, 20000)
        shots[label] = shot.get("path") or shot.get("shot")
    world(game, "camera_release", {}, 8000)

    ok_names = bool(names)
    print("%s names: %d foreign NPC name(s) from %s" % ("PASS" if ok_names else "FAIL", len(names), origin_of_names))
    print("%s drawn: %d of %d foreign NPC(s) drawn in the world%s" % (
        "PASS" if not drawn else "FAIL", len(drawn), len(names),
        "" if not drawn else " - " + ", ".join("%s near %s" % (r["name"], r["nearest"]) for r in drawn)))
    print("INFO creator shots: " + ", ".join(creator_shots))
    print("RESULT " + json.dumps({"label": a.label, "names": names, "drawn": len(drawn), "npcs": rows,
                                  "creatorShots": creator_shots, "shots": shots}))
    sys.exit(0 if ok_names and not drawn else 1)


if __name__ == "__main__":
    main()
