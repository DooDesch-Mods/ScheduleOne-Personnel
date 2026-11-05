# Sends one raw ScheduleMCP bridge command to a slot's game and prints the JSON answer.
#
#     python -I tests/bridge_call.py --slot mptest4 save_now
#     python -I tests/bridge_call.py --slot mptest4 load_save "{\"slot\": 5}" --timeout 90000
#
# Exit 0 when the bridge answered ok, 1 otherwise (the answer is printed either way).
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Workspace", "tools", "bridge"))
import s1  # noqa: E402

p = argparse.ArgumentParser()
p.add_argument("--slot", required=True)
p.add_argument("command")
p.add_argument("args", nargs="?", default="{}")
p.add_argument("--timeout", type=int, default=12000)
a = p.parse_args()

game = s1.Game(a.slot)
try:
    answer = game.call(a.command, json.loads(a.args), timeout_ms=a.timeout)
except Exception as e:  # BridgeDown, TimeoutError: report and fail
    print("bridge %s failed: %s: %s" % (a.command, type(e).__name__, e))
    sys.exit(1)
print(json.dumps(answer))
sys.exit(0 if answer.get("ok") else 1)
