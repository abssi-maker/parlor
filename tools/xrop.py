#!/usr/bin/env python3
"""Call Meta XR Operator tools on the app running in Play mode (Meta XR Simulator), over its local MCP/SSE server.
Usage: xrop.py list
       xrop.py <tool> ['<json args>'] [--out file]      (image results are written to --out, or /dev/null)
       xrop.py -            reads lines from stdin, one session: "<tool> <json args> [--out file]", "sleep <s>",
                            "eval <C# for the Editor>", "until <seconds> <console text>"
The server (http://127.0.0.1:8720/sse) exists only while the app is running."""
import base64, http.client, json, queue, sys, threading, time

HOST, PORT = "127.0.0.1", 8720


class Session:
    def __init__(self):
        self.q = queue.Queue(); self.endpoint = None; self.id = 0
        self.conn = http.client.HTTPConnection(HOST, PORT, timeout=60)
        self.conn.request("GET", "/sse", headers={"Accept": "text/event-stream"})
        self.resp = self.conn.getresponse()
        threading.Thread(target=self._read, daemon=True).start()
        t0 = time.time()
        while self.endpoint is None and time.time() - t0 < 10: time.sleep(0.02)
        if self.endpoint is None: raise RuntimeError("no endpoint event from XR Operator")
        self.rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "parlor", "version": "1"}})
        self.post({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def _read(self):
        event, data = None, []
        for raw in self.resp:
            line = raw.decode("utf-8", "replace").rstrip("\r\n")
            if line.startswith("event:"): event = line[6:].strip()
            elif line.startswith("data:"): data.append(line[5:].lstrip())
            elif line == "":
                payload = "\n".join(data)
                if event == "endpoint": self.endpoint = payload
                elif payload:
                    try: self.q.put(json.loads(payload))
                    except ValueError: pass
                event, data = None, []

    def post(self, body):
        c = http.client.HTTPConnection(HOST, PORT, timeout=60)
        c.request("POST", self.endpoint, body=json.dumps(body), headers={"Content-Type": "application/json"})
        c.getresponse().read(); c.close()

    def rpc(self, method, params, timeout=60):
        self.id += 1; mine = self.id
        self.post({"jsonrpc": "2.0", "id": mine, "method": method, "params": params})
        end = time.time() + timeout
        while time.time() < end:
            try: m = self.q.get(timeout=1)
            except queue.Empty: continue
            if isinstance(m, dict) and m.get("id") == mine:
                if "error" in m: raise RuntimeError(json.dumps(m["error"]))
                return m["result"]
        raise TimeoutError(method)

    def call(self, tool, args, out=None):
        r = self.rpc("tools/call", {"name": tool, "arguments": args})
        texts = []
        for c in r.get("content", []):
            if c.get("type") == "image":
                if out:
                    open(out, "wb").write(base64.b64decode(c["data"])); texts.append("image -> " + out)
                else: texts.append("image (%d b64 chars, not saved)" % len(c["data"]))
            else: texts.append(c.get("text", ""))
        return ("ERROR " if r.get("isError") else "") + "\n".join(texts)


def main():
    a = sys.argv[1:]
    out = None
    if "--out" in a: i = a.index("--out"); out = a[i + 1]; del a[i:i + 2]
    s = Session()
    if a[0] == "list":
        for t in s.rpc("tools/list", {})["tools"]:
            props = t.get("inputSchema", {}).get("properties", {})
            print(t["name"], "(" + ", ".join(props) + ")")
            if len(a) > 1: print("   ", (t.get("description") or "")[:600].replace("\n", " "), "\n   ", json.dumps(props)[:900])
    elif a[0] == "-":
        for line in sys.stdin:
            line = line.strip()
            if not line or line.startswith("#"): continue
            name, _, rest = line.partition(" ")
            if name == "sleep": time.sleep(float(rest)); continue
            if name == "eval":   # C# in the running Editor, e.g. eval Parlor.ParlorApp.Instance.Replace(); return "ok";
                import subprocess
                r = subprocess.run(["unity", "command", "script", "eval", "--timeout", "60", "--code", rest], capture_output=True, text=True)
                print("eval =>", (r.stdout.strip().splitlines() or [""])[-1][:300], flush=True); continue
            if name == "shot":   # shot <file.png>: the Meta XR Simulator window (virtual content over the synthetic room)
                import subprocess, os
                r = subprocess.run(["screencapture", "-x", "-o", "-l", os.environ.get("XRSIM_WINDOW", "0"), rest.strip()], capture_output=True, text=True)
                print("shot =>", rest.strip() if r.returncode == 0 else "FAILED " + r.stderr[:200], flush=True); continue
            if name in ("hand", "handstate"):
                import subprocess
                def ev(code):
                    r = subprocess.run(["unity", "command", "script", "eval", "--timeout", "60", "--code", code], capture_output=True, text=True)
                    return (r.stdout.strip().splitlines() or [""])[-1]
                if name == "handstate": print("hands =>", ev("return Parlor.DebugSeat.Hands();")[:500], flush=True); continue
                # hand <left|right> <gesture> [@point] [json of extra arguments]
                parts = rest.split(None, 3)
                args = {"hand": parts[0], "gesture": parts[1]}
                extra = parts[2:]
                if extra and extra[0].startswith("@"):
                    xyz = ev('return Parlor.DebugSeat.OpenXrPoint("%s");' % extra[0][1:]).split()
                    if len(xyz) != 3: print("hand => no point", extra[0], flush=True); continue
                    # "open" takes no target: the point becomes the wrist position
                    args["wrist_position" if parts[1] == "open" else "target"] = [float(v) for v in xyz]; extra = extra[1:]
                if extra: args.update(json.loads(" ".join(extra)))
                print("hand", parts[0], parts[1], "=>", s.call("openxr_hand_gesture", args).replace("\n", " ")[:300], flush=True); continue
            if name == "look":   # look @point [duration]: turn the simulated head towards a named point of the show
                import subprocess
                parts = rest.split()
                r = subprocess.run(["unity", "command", "script", "eval", "--timeout", "60", "--code",
                                    'return Parlor.DebugSeat.OpenXrLook("%s");' % parts[0].lstrip("@")], capture_output=True, text=True)
                nums = (r.stdout.strip().splitlines() or [""])[-1].split()
                if len(nums) != 7: print("look => failed", flush=True); continue
                v = [float(x) for x in nums]
                a = {"position": v[:3], "orientation": v[3:]}
                if len(parts) > 1: a["duration_seconds"] = float(parts[1])
                print("look", parts[0], "=>", s.call("openxr_set_head_pose", a).replace("\n", " ")[:80], flush=True); continue
            if name == "seat":   # seat [index] [pitch]: sit the simulated head at a table of the loaded room
                import subprocess
                parts = rest.split(); idx = parts[0] if parts else "0"; pitch = parts[1] if len(parts) > 1 else "32"
                r = subprocess.run(["unity", "command", "script", "eval", "--timeout", "60", "--code",
                                    "return Parlor.DebugSeat.OpenXrSeat(%s, %sf);" % (idx, pitch)], capture_output=True, text=True)
                line = (r.stdout.strip().splitlines() or [""])[-1]
                nums = line.split("|")[0].split()
                if len(nums) != 7: print("seat => no usable table:", line[:200], flush=True); continue
                v = [float(x) for x in nums]
                print("seat =>", line, s.call("openxr_set_head_pose", {"position": v[:3], "orientation": v[3:]}).replace("\n", " "), flush=True); continue
            if name == "until":  # until <seconds> <text>: wait for a console line containing text
                import subprocess
                secs, _, text = rest.partition(" "); end = time.time() + float(secs)
                while time.time() < end:
                    r = subprocess.run(["unity", "command", "console", "get", "--tail", "400"], capture_output=True, text=True)
                    if text in r.stdout: break
                    time.sleep(1.5)
                print("until", text, "=>", "seen" if time.time() < end else "TIMEOUT", flush=True); continue
            o = None
            if " --out " in rest: rest, _, o = rest.partition(" --out ")
            print(name, "=>", s.call(name, json.loads(rest) if rest.strip() else {}, o.strip() if o else None)[:400], flush=True)
    else:
        print(s.call(a[0], json.loads(a[1]) if len(a) > 1 else {}, out))


if __name__ == "__main__":
    main()
