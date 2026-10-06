"""Drive a Hyper Testing clone over HTTP JSON-RPC when its MCP tools are not loaded in the agent.

usage:
  python hc.py <hcN|port> <tool> [args.json|-] [timeoutSec] [--full]
  python hc.py <hcN|port> --body file.cs [timeoutSec]     script-execute, the file is a method body
  python hc.py <hcN|port> --class file.cs [timeoutSec]    script-execute, the file defines class HT with static void Main()

  hcN | port   a clone name (looked up in Library/MatrixHyperTesting/pool.json) or a port number
  tool         an MCP tool name, for example script-execute, console-get-logs, screenshot-game-view
  args.json    a file holding the tool arguments as JSON, or - for none
  --full       print the whole reply instead of the first 6000 characters

--body and --class print "OK <text>" or "ERR <text>" (first 2500 characters of the tool text or the error).
"""
import json
import os
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))


def port_of(target):
    if target.isdigit():
        return int(target)
    index = int(target.lower()[2:] if target.lower().startswith("hc") else target)
    pool = os.path.join(HERE, "..", "..", "Library", "MatrixHyperTesting", "pool.json")
    with open(pool, encoding="utf-8-sig") as handle:
        for clone in json.load(handle)["clones"]:
            if clone["index"] == index:
                return clone["port"]
    sys.exit("hc%d is not listed in %s" % (index, os.path.normpath(pool)))


def post(url, body, session=None, timeout=60.0):
    headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
    if session:
        headers["Mcp-Session-Id"] = session
    request = urllib.request.Request(url, json.dumps(body).encode(), headers)
    with urllib.request.urlopen(request, timeout=timeout) as reply:
        text = reply.read().decode("utf-8", "replace")
        session = reply.headers.get("Mcp-Session-Id", session)
    data = [line[5:].strip() for line in text.splitlines() if line.startswith("data:")]
    return (json.loads(data[-1]) if data else None), session


def call(port, tool, arguments, timeout=60.0):
    url = "http://localhost:%d/mcp" % port
    init = {"jsonrpc": "2.0", "id": 1, "method": "initialize",
            "params": {"protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "orchestrator", "version": "1"}}}
    _, session = post(url, init, None, timeout)
    post(url, {"jsonrpc": "2.0", "method": "notifications/initialized"}, session, timeout)
    reply, _ = post(url, {"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": tool, "arguments": arguments}}, session, timeout)
    return reply


def verdict(reply):
    if not isinstance(reply, dict):
        return "ERR empty reply from the clone"
    if reply.get("error"):
        return "ERR rpc error: %s" % json.dumps(reply["error"], ensure_ascii=False)[:2500]
    result = reply.get("result") or {}
    content = result.get("content") or [{}]
    text = (content[0].get("text") or "")[:2500]
    return ("ERR " if result.get("isError") else "OK ") + text


def main(argv):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    full = "--full" in argv
    argv = [a for a in argv if a != "--full"]
    if len(argv) < 2:
        sys.exit(__doc__)
    port = port_of(argv[0])
    if argv[1] in ("--body", "--class"):
        if len(argv) < 3:
            sys.exit(__doc__)
        with open(argv[2], encoding="utf-8") as handle:
            code = handle.read()
        arguments = {"csharpCode": code, "isMethodBody": True} if argv[1] == "--body" else {"csharpCode": code, "isMethodBody": False, "className": "HT", "methodName": "Main"}
        print(verdict(call(port, "script-execute", arguments, float(argv[3]) if len(argv) > 3 else 120.0)))
        return
    arguments = {}
    if len(argv) > 2 and argv[2] != "-":
        with open(argv[2], encoding="utf-8") as handle:
            arguments = json.load(handle)
    timeout = float(argv[3]) if len(argv) > 3 else 60.0
    text = json.dumps(call(port, argv[1], arguments, timeout), ensure_ascii=False)
    print(text if full else text[:6000])


if __name__ == "__main__":
    main(sys.argv[1:])
