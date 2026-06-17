"""
Local WebGL dev server — serves Brotli Unity build + proxies API calls to GCP.
Usage: python serve_webgl.py
Opens http://127.0.0.1:8081
"""
import http.server
import os
import sys
import urllib.request
import urllib.error

HOST = "127.0.0.1"
PORT = 8081
SERVE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Builds", "WebGL")

# All API calls are forwarded to the real GCP registry server.
GCP_REGISTRY = "http://35.240.203.91"
API_PREFIXES = ("/create", "/api/", "/s/", "/play")


class WebGLHandler(http.server.SimpleHTTPRequestHandler):
    _BR_MAP = {
        ".wasm.br":         ("application/wasm",         "br"),
        ".js.br":           ("application/javascript",   "br"),
        ".data.br":         ("application/octet-stream", "br"),
        ".symbols.json.br": ("application/json",         "br"),
    }
    _PLAIN_MAP = {
        ".wasm":  "application/wasm",
        ".js":    "application/javascript",
        ".data":  "application/octet-stream",
    }

    # ── API proxy ──────────────────────────────────────────────────────────────

    def _is_api(self):
        path = self.path.split("?")[0]
        return any(path == p or path.startswith(p) for p in API_PREFIXES)

    def _proxy(self):
        target = GCP_REGISTRY + self.path
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length) if length else None

        # Forward relevant headers
        fwd_headers = {}
        for h in ("Content-Type", "Accept", "Authorization"):
            if self.headers.get(h):
                fwd_headers[h] = self.headers[h]

        req = urllib.request.Request(target, data=body,
                                     headers=fwd_headers, method=self.command)
        try:
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = resp.read()
                self.send_response(resp.status)
                for k, v in resp.headers.items():
                    if k.lower() in ("content-type", "content-length",
                                     "access-control-allow-origin"):
                        self.send_header(k, v)
                self.send_header("Access-Control-Allow-Origin", "*")
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                self.wfile.write(data)
        except urllib.error.HTTPError as e:
            data = e.read()
            self.send_response(e.code)
            self.send_header("Content-Type",
                             e.headers.get("Content-Type", "text/plain"))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(data)
        except Exception as exc:
            self.send_error(502, f"Proxy error: {exc}")

    def do_GET(self):
        if self._is_api():
            self._proxy()
        else:
            super().do_GET()

    def do_POST(self):
        if self._is_api():
            self._proxy()
        else:
            self.send_error(405)

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization")
        self.end_headers()

    # ── Brotli MIME fix ────────────────────────────────────────────────────────

    def guess_type(self, path):
        clean = path.split("?")[0]
        for suffix, (mime, _) in self._BR_MAP.items():
            if clean.endswith(suffix):
                return mime
        for suffix, mime in self._PLAIN_MAP.items():
            if clean.endswith(suffix):
                return mime
        return super().guess_type(path)

    def end_headers(self):
        clean = self.path.split("?")[0]
        for suffix, (_, enc) in self._BR_MAP.items():
            if clean.endswith(suffix):
                self.send_header("Content-Encoding", enc)
                break
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, fmt, *args):
        tag = "[proxy]" if self._is_api() else "[file] "
        print(f"  {tag} {self.command} {self.path}  →  {fmt % args}")


if __name__ == "__main__":
    if not os.path.isdir(SERVE_DIR):
        print(f"[ERROR] Build directory not found: {SERVE_DIR}")
        print("        Run 'Tools/Tank MAPF/Build WebGL Client' in Unity first.")
        sys.exit(1)

    os.chdir(SERVE_DIR)
    server = http.server.HTTPServer((HOST, PORT), WebGLHandler)
    print(f"[WebGL Server] Static:  {SERVE_DIR}")
    print(f"[WebGL Server] Proxy:   /create /api/ /s/ → {GCP_REGISTRY}")
    print(f"[WebGL Server] Open:    http://{HOST}:{PORT}")
    print(f"[WebGL Server] Ctrl+C to stop.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[WebGL Server] Stopped.")
