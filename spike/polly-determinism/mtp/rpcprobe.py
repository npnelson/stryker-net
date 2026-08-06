#!/usr/bin/env python3
"""Stand in for Stryker's end of the MTP server-mode connection and dump the handshake.

Stryker listens and the test host dials in, so this listens on a port, launches the
host exactly the way DefaultTestServerConnectionFactory does, and prints every
JSON-RPC frame in both directions until the host goes away.
"""
import json
import socket
import subprocess
import sys
import threading

ASSEMBLY = sys.argv[1]
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 45123


def read_message(f):
    headers = {}
    while True:
        line = f.readline()
        if not line:
            return None
        line = line.strip()
        if not line:
            break
        k, _, v = line.decode().partition(":")
        headers[k.strip().lower()] = v.strip()
    n = int(headers.get("content-length", 0))
    return json.loads(f.read(n)) if n else None


def send(sock, obj):
    body = json.dumps(obj).encode()
    sock.sendall(f"Content-Length: {len(body)}\r\n\r\n".encode() + body)
    print(f"--> {json.dumps(obj)[:400]}", flush=True)


srv = socket.socket()
srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(("127.0.0.1", PORT))
srv.listen(1)

proc = subprocess.Popen(
    ["dotnet", ASSEMBLY, "--server", "--client-port", str(PORT)],
    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)


def drain():
    for line in proc.stdout:
        print(f"[host] {line.rstrip()}", flush=True)


threading.Thread(target=drain, daemon=True).start()

discovered = []
conn, _ = srv.accept()
print("host connected", flush=True)
f = conn.makefile("rb")

# Stryker's first call: initialize
send(conn, {"jsonrpc": "2.0", "id": 1, "method": "initialize",
            "params": {"processId": 1234,
                       "clientInfo": {"name": "stryker-probe", "version": "1.0.0"},
                       "capabilities": {"testing": {"debuggerProvider": False}}}})

while True:
    try:
        msg = read_message(f)
    except Exception as exc:
        print(f"read failed: {exc!r}", flush=True)
        break
    if msg is None:
        print("stream closed by host", flush=True)
        break
    print(f"<-- {json.dumps(msg)[:600]}", flush=True)
    if msg.get("id") == 1 and "result" in msg:
        send(conn, {"jsonrpc": "2.0", "method": "initialized", "params": {}})
        send(conn, {"jsonrpc": "2.0", "id": 2, "method": "testing/discoverTests",
                    "params": {"runId": "00000000-0000-0000-0000-000000000001"}})
    if msg.get("method") == "testing/testUpdates/tests":
        for ch in (msg["params"].get("changes") or []):
            discovered.append(ch["node"])
    if msg.get("id") == 2 and "result" in msg:
        print(f"discovered {len(discovered)} tests; requesting a run of the first 3",
              flush=True)
        send(conn, {"jsonrpc": "2.0", "id": 3, "method": "testing/runTests",
                    "params": {"runId": "00000000-0000-0000-0000-000000000002",
                               "tests": [{k: v for k, v in n.items()
                                          if k in ("uid", "display-name", "node-type",
                                                   "execution-state")}
                                         for n in discovered[:3]]}})
    if msg.get("id") == 3:
        print(f"run #1 answered: {json.dumps(msg)[:200]}", flush=True)
        print("--- now sending a SECOND run request on the same host ---", flush=True)
        send(conn, {"jsonrpc": "2.0", "id": 4, "method": "testing/runTests",
                    "params": {"runId": "00000000-0000-0000-0000-000000000003",
                               "tests": [{k: v for k, v in n.items()
                                          if k in ("uid", "display-name", "node-type",
                                                   "execution-state")}
                                         for n in discovered[3:6]]}})
    if msg.get("id") == 4:
        print(f"run #2 answered: {json.dumps(msg)[:200]}", flush=True)
        break

proc.terminate()
print(f"host exit code: {proc.wait()}", flush=True)
