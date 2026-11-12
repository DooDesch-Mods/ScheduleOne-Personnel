"""Checks that a supplier keeps offering dead-drop orders in the phone, with Personnel (and Based) installed.

Albert Hoover is the supplier unlocked in the 'played' template. The script opens his conversation in the
Messages app the way a player does (phone, app icon, conversation, compose) and reads which answers are offered.
Vanilla hides "I need to order a dead drop" while an order is being prepared, so the check orders through the
Debug probe (`personnel supplier <id> order`), expects the answer to be hidden, lets the game clock run until the
drop is ready and expects the answer back. The same again across save and load. A first order runs before any
phone check: its "drop is ready" text moves the supplier to the top of the Messages list.

    pwsh -File ../Workspace/tools/session/session.ps1 -Build Personnel,Based -NoApp
    python -I tests/supplier_order_check.py                    # the slot this session holds
    python -I tests/supplier_order_check.py --slot mptest3 --no-reload

Prints PASS or FAIL per check and exits 1 when any check fails. Needs a Debug build of Personnel (the probe).
"""
import argparse
import os
import re
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Workspace", "tools", "bridge"))
import s1  # noqa: E402

ORDER = "I need to order a dead drop"
DEBT = "I want to pay off my debt"
results = []


def report(name, ok, detail=""):
    results.append(ok)
    print("%s  %s%s" % ("PASS" if ok else "FAIL", name, (" - " + detail) if detail else ""), flush=True)


def probe(g, supplier, action):
    r = g.run("personnel supplier %s %s" % (supplier, action))
    lines = (r.get("result") or {}).get("lines") or []
    text = "\n".join(l.get("text", "") for l in lines if "supplier" in l.get("text", ""))
    if not text:
        text = str(r.get("result") or r.get("error"))
    return text


def state(g, supplier):
    text = probe(g, supplier, "dump")
    m = re.search(r"unlocked=(\w+).*?debt=([-\d.]+).*?minsUntilReady=(-?\d+)", text)
    if not m:
        print("info  dump answered without a state line: %s" % text[:300], flush=True)
        return None
    return {"unlocked": m.group(1) == "True", "debt": float(m.group(2)), "mins": int(m.group(3))}


def click(g, query, index=0):
    r = g.call("ui_click", {"query": query, "index": index})
    res = r.get("result") or {}
    return bool(r.get("ok")) and res.get("onTarget", False), res, r.get("error")


def find(g, query):
    r = g.call("ui_find", {"query": query})
    return (r.get("result") or {}).get("elements") or []


def offered_answers(g, name):
    """Opens the supplier's conversation and returns the labels of the answers the compose sheet shows."""
    g.call("phone", {"open": True})
    time.sleep(0.8)
    # Back to the conversation list if a conversation is already open.
    click(g, "DialogueScreen/Topbar/Return")
    time.sleep(0.4)
    # The Messages app keeps its last screen; from the home screen its icon may sit further down the icon list.
    if not find(g, "Messages/Container/Home"):
        for _ in range(6):
            icons = [e for e in find(g, "AppIcon") if (e.get("label") or "").endswith("Messages")]
            if icons:
                click(g, icons[0]["label"])
                time.sleep(0.8)
                break
            seen = [e.get("label") for e in find(g, "AppIcons/") if e.get("label")]
            print("info  no Messages icon on screen, icons seen: %s" % seen, flush=True)
            click(g, "AppIconsScrollView/Scrollbar")
            time.sleep(0.5)
    entry = "ConversationEntry_%s/Button" % name
    opened = False
    for _ in range(12):
        ok, res, _err = click(g, entry)
        if ok:
            opened = True
            break
        # The entry is off screen or under the hotbar: a click on the scrollbar track pages down.
        click(g, "Home/Scroll View/Scrollbar Vertical")
        time.sleep(0.4)
    for _ in range(40):
        if opened:
            break
        # Paging stops once the scrollbar handle covers its centre. Hiding the conversations listed above the
        # supplier moves it up instead; the save is a test copy.
        hides = [e["path"] for e in find(g, "ConversationEntry_") if e.get("path", "").endswith("/Hide")
                 and name not in e["path"]]
        # ui_find also lists entries scrolled out of the mask; the first one a click really reaches is hidden.
        if not any(click(g, h.split("/Content/")[-1])[0] for h in hides):
            break
        time.sleep(0.4)
        ok, _res, _err = click(g, entry)
        opened = ok
    if not opened:
        print("info  conversation entry %r never came on screen" % name, flush=True)
        return None
    time.sleep(0.8)
    ok, _res, err = click(g, "ComposeButton")
    if not ok:
        print("info  compose button not clickable: %s" % err, flush=True)
        return None
    time.sleep(0.8)
    labels = [e.get("label") for e in find(g, "Sendables") if e.get("label")]
    click(g, "Cancel")
    g.call("phone", {"open": False})
    time.sleep(0.4)
    return labels


def wait_ready_drop(g, supplier, timeout):
    end = time.time() + timeout
    st = None
    while time.time() < end:
        st = state(g, supplier)
        if st and st["mins"] < 0:
            return True, st
        time.sleep(4)
    return False, st


def order_cycle(g, args, tag):
    out = probe(g, args.supplier, "order %s 1" % args.item)
    report("%s: order placed" % tag, "ordered" in out, out.strip().splitlines()[-1][:160] if out.strip() else "no answer")
    st = state(g, args.supplier)
    report("%s: order is being prepared" % tag, bool(st) and st["mins"] > 0, str(st))
    return st


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", default=None)
    ap.add_argument("--supplier", default="albert_hoover")
    ap.add_argument("--name", default="Albert Hoover")
    ap.add_argument("--item", default="ogkushseed")
    ap.add_argument("--timeout", type=int, default=120, help="seconds to wait for a drop to be ready")
    ap.add_argument("--no-reload", action="store_true")
    ap.add_argument("--save", type=int, default=5)
    args = ap.parse_args()
    g = s1.Game(slot=args.slot)

    st = state(g, args.supplier)
    report("supplier is unlocked", bool(st) and st["unlocked"], str(st))
    if st and st["mins"] > 0:
        ok, st = wait_ready_drop(g, args.supplier, args.timeout)
        print("info  an earlier order was still open; ready=%s" % ok, flush=True)

    # A finished drop makes the supplier text "your drop is ready", which moves the conversation to the top of
    # the Messages list. On a played save the supplier otherwise sits below dozens of customer threads.
    order_cycle(g, args, "warm-up")
    ok, st = wait_ready_drop(g, args.supplier, args.timeout)
    report("warm-up: drop becomes ready", ok, str(st))
    time.sleep(2)

    labels = offered_answers(g, args.name)
    report("phone offers the dead-drop order", bool(labels) and ORDER in labels, str(labels))
    report("phone offers the debt answer", bool(labels) and DEBT in labels, str(labels))

    order_cycle(g, args, "live")
    labels = offered_answers(g, args.name)
    report("live: order answer hidden while the drop is prepared (vanilla)", labels is not None and ORDER not in labels, str(labels))
    ok, st = wait_ready_drop(g, args.supplier, args.timeout)
    report("live: drop becomes ready", ok, str(st))
    labels = offered_answers(g, args.name)
    report("live: order answer is back", bool(labels) and ORDER in labels, str(labels))

    if not args.no_reload:
        order_cycle(g, args, "reload")
        g.call("save_now", {}, timeout_ms=30000)
        time.sleep(3)
        g.call("return_to_menu", {}, timeout_ms=30000)
        # The load is refused while the game is still leaving the save, so wait for the menu first.
        end = time.time() + 120
        while time.time() < end:
            time.sleep(3)
            try:
                if not (g.call("get_state", timeout_ms=4000).get("result") or {}).get("inGame"):
                    break
            except (s1.BridgeDown, TimeoutError):
                pass
        time.sleep(3)
        r = g.call("load_save_by_slot", {"slot": args.save}, timeout_ms=30000)
        print("info  load save %d: ok=%s %s" % (args.save, r.get("ok"), r.get("error") or ""), flush=True)
        g.wait_ready(timeout=300)
        st = state(g, args.supplier)
        report("reload: order survives save and load", bool(st) and st["mins"] > 0, str(st))
        ok, st = wait_ready_drop(g, args.supplier, args.timeout)
        report("reload: drop becomes ready", ok, str(st))
        labels = offered_answers(g, args.name)
        report("reload: order answer is back", bool(labels) and ORDER in labels, str(labels))

    failed = results.count(False)
    print("%d/%d PASS" % (len(results) - failed, len(results)), flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
