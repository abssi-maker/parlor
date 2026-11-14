#!/usr/bin/env python3
"""Call one tool on the metavr built-in MCP server over stdio.
Usage: mcpcall.py <tool> '<json args>' [timeout_seconds]"""
import json, subprocess, sys, os, select, time
tool, args = sys.argv[1], json.loads(sys.argv[2] if len(sys.argv) > 2 else "{}")
timeout = float(sys.argv[3]) if len(sys.argv) > 3 else 120
p = subprocess.Popen([os.path.expanduser("~/.metavr/bin/metavr"), "mcp", "server"],
                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
def send(o): p.stdin.write(json.dumps(o) + "\n"); p.stdin.flush()
send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2024-11-05",
      "capabilities": {}, "clientInfo": {"name": "agent", "version": "1"}}})
send({"jsonrpc": "2.0", "method": "notifications/initialized"})
send({"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": tool, "arguments": args}})
end = time.time() + timeout
while time.time() < end:
    r, _, _ = select.select([p.stdout], [], [], 1)
    if not r: continue
    line = p.stdout.readline()
    if not line: break
    try: d = json.loads(line)
    except ValueError: continue
    if d.get("id") == 2:
        if "error" in d: print("ERROR", json.dumps(d["error"])); break
        for c in d["result"].get("content", []):
            print(c.get("text", "<%s>" % c.get("type")))
        break
else:
    print("TIMEOUT")
p.kill()
