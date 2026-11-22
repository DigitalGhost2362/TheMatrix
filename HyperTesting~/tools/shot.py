"""Save the Game view of a clone to a PNG over HTTP.

usage: python shot.py <hcN|port> out.png
"""
import base64
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def main(argv):
    if len(argv) != 2:
        sys.exit(__doc__)
    out = subprocess.run([sys.executable, os.path.join(HERE, "hc.py"), argv[0], "screenshot-game-view", "-", "60", "--full"],
                         capture_output=True, text=True, encoding="utf-8").stdout
    reply = json.loads(out)
    for item in (reply.get("result") or {}).get("content", []):
        if item.get("type") == "image":
            with open(argv[1], "wb") as handle:
                handle.write(base64.b64decode(item["data"]))
            print("saved", argv[1])
            return
    print(out[:500])


if __name__ == "__main__":
    main(sys.argv[1:])
