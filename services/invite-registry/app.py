import json
import os
import secrets
import html as html_module
import time
import subprocess
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse


DEFAULT_HOST = "0.0.0.0"
DEFAULT_PORT = 8080
DEFAULT_PUBLIC_BASE_URL = "http://127.0.0.1:8080"
DEFAULT_ADMIN_TOKEN = "dev-token"
DEFAULT_DATA_FILE = os.path.join(
    os.path.dirname(__file__),
    "data",
    "sessions.json",
)
DEFAULT_CREATE_ROOM_HELPER = "/usr/local/bin/tank-mapf-create-room"


def env_string(name, default):
    value = os.environ.get(name)
    return value.strip() if value and value.strip() else default


def env_int(name, default):
    value = os.environ.get(name)
    if value is None:
        return default
    try:
        return int(value)
    except ValueError:
        return default


REGISTRY_HOST = env_string("REGISTRY_HOST", DEFAULT_HOST)
REGISTRY_PORT = env_int("REGISTRY_PORT", DEFAULT_PORT)
REGISTRY_PUBLIC_BASE_URL = env_string("REGISTRY_PUBLIC_BASE_URL", DEFAULT_PUBLIC_BASE_URL).rstrip("/")
REGISTRY_ADMIN_TOKEN = env_string("REGISTRY_ADMIN_TOKEN", DEFAULT_ADMIN_TOKEN)
REGISTRY_DATA_FILE = env_string("REGISTRY_DATA_FILE", DEFAULT_DATA_FILE)
CREATE_ROOM_HELPER = env_string("CREATE_ROOM_HELPER", DEFAULT_CREATE_ROOM_HELPER)


def ensure_data_dir():
    os.makedirs(os.path.dirname(REGISTRY_DATA_FILE), exist_ok=True)


def load_sessions():
    ensure_data_dir()
    if not os.path.exists(REGISTRY_DATA_FILE):
        return {}

    with open(REGISTRY_DATA_FILE, "r", encoding="utf-8") as handle:
        payload = json.load(handle)

    if not isinstance(payload, dict):
        return {}

    sessions = payload.get("sessions", payload)
    return sessions if isinstance(sessions, dict) else {}


def save_sessions(sessions):
    ensure_data_dir()
    payload = {
        "sessions": sessions,
        "updatedAt": int(time.time()),
    }

    temp_path = REGISTRY_DATA_FILE + ".tmp"
    with open(temp_path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
    os.replace(temp_path, REGISTRY_DATA_FILE)


def prune_expired_sessions(sessions, now=None):
    now = int(time.time()) if now is None else int(now)
    expired_codes = [
        code for code, session in sessions.items()
        if int(session.get("expiresAt", 0)) <= now
    ]
    for code in expired_codes:
        sessions.pop(code, None)
    return bool(expired_codes)


def make_session_response(session):
    payload = {
        "code": session["code"],
        "host": session["host"],
        "gamePort": session["gamePort"],
        "transport": session.get("transport", "udp"),
        "map": session["map"],
        "algorithm": session["algorithm"],
        "maxPlayers": session["maxPlayers"],
        "expiresAt": session["expiresAt"],
    }
    if session.get("webHost"):
        payload["webHost"] = session["webHost"]
    if session.get("webGamePort"):
        payload["webGamePort"] = session["webGamePort"]
    if session.get("webTransport"):
        payload["webTransport"] = session["webTransport"]
    if session.get("webUrl"):
        payload["webUrl"] = session["webUrl"]
    return payload


def build_join_url(code):
    return f"{REGISTRY_PUBLIC_BASE_URL}/s/{code}"


class InviteRegistryHandler(BaseHTTPRequestHandler):
    server_version = "TankMapfRegistry/0.1"

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        if path == "/healthz":
            self.write_json(200, {"ok": True})
            return

        if path.startswith("/api/sessions/"):
            code = path.rsplit("/", 1)[-1].strip().upper()
            self.handle_get_session(code)
            return

        if path.startswith("/s/"):
            code = path.rsplit("/", 1)[-1].strip().upper()
            self.handle_session_page(code)
            return

        if path == "/create":
            self.handle_create_room_page()
            return

        self.write_json(404, {"error": "Not found"})

    def do_POST(self):
        parsed = urlparse(self.path)
        if parsed.path not in {"/api/sessions", "/api/admin/rooms", "/api/rooms"}:
            self.write_json(404, {"error": "Not found"})
            return

        if parsed.path in {"/api/sessions", "/api/admin/rooms"} and not self.is_authorized():
            self.write_json(401, {"error": "Unauthorized"})
            return

        if "application/json" not in (self.headers.get("Content-Type") or ""):
            self.write_json(400, {"error": "Content-Type must be application/json"})
            return

        length = int(self.headers.get("Content-Length") or 0)
        try:
            raw_body = self.rfile.read(length).decode("utf-8")
            body = json.loads(raw_body) if raw_body else {}
        except (UnicodeDecodeError, json.JSONDecodeError):
            self.write_json(400, {"error": "Invalid JSON body"})
            return

        try:
            session = self.create_room(body) if parsed.path in {"/api/admin/rooms", "/api/rooms"} else self.build_session(body)
        except ValueError as exc:
            self.write_json(400, {"error": str(exc)})
            return

        sessions = load_sessions()
        changed = prune_expired_sessions(sessions)
        sessions[session["code"]] = session
        save_sessions(sessions)

        if changed:
            print("[Registry] Pruned expired sessions before insert.")
        print(f"[Registry] Upserted session {session['code']} -> {session['host']}:{session['gamePort']}")

        self.write_json(201, {
            "code": session["code"],
            "joinUrl": build_join_url(session["code"]),
            "webUrl": session.get("webUrl", ""),
        })

    def handle_get_session(self, code):
        if not code:
            self.write_json(400, {"error": "Session code is required"})
            return

        sessions = load_sessions()
        if prune_expired_sessions(sessions):
            save_sessions(sessions)

        session = sessions.get(code)
        if session is None:
            self.write_json(404, {"error": "Session not found"})
            return

        self.write_json(200, make_session_response(session))

    def handle_session_page(self, code):
        if not code:
            self.write_html(400, "<h1>Invalid session</h1>")
            return

        sessions = load_sessions()
        if prune_expired_sessions(sessions):
            save_sessions(sessions)

        session = sessions.get(code)
        if session is None:
            self.write_html(404, "<h1>Session not found</h1>")
            return

        web_url = session.get("webUrl", "").strip()
        if web_url:
            self.write_redirect(302, web_url)
            return

        endpoint = f"{session['host']}:{session['gamePort']}"
        html_body = (
            "<!doctype html><html><head><meta charset=\"utf-8\">"
            f"<title>Tank MAPF Session {session['code']}</title></head><body>"
            f"<h1>Tank MAPF Session {session['code']}</h1>"
            f"<p>Endpoint: {endpoint}</p>"
            f"<p>Session code: {session['code']}</p>"
        )
        if web_url:
            safe_url = html_module.escape(web_url, quote=True)
            html_body += f"<p><a href=\"{safe_url}\">Play in browser</a></p>"
        html_body += "<p>Open the game and paste this code or link.</p></body></html>"
        self.write_html(200, html_body)

    def handle_create_room_page(self):
        html = """<!doctype html><html><head><meta charset="utf-8"><title>Tank MAPF Create Room</title>
<style>
body{font-family:system-ui,sans-serif;margin:24px;max-width:760px}
label{display:block;margin:12px 0 6px}
select,input,button{padding:10px;font-size:16px;width:100%;box-sizing:border-box}
button{margin-top:16px}
pre{background:#111;color:#0f0;padding:12px;white-space:pre-wrap}
</style></head><body>
<h1>Create Room</h1>
<label>Map</label><select id="map">
  <option value="random-32-32-10.map">random-32-32-10.map</option>
  <option value="ht_mansion_n.map">ht_mansion_n.map</option>
  <option value="ht_chantry.map">ht_chantry.map</option>
  <option value="lt_gallowstemplar_n.map">lt_gallowstemplar_n.map</option>
  <option value="maze-128-128-10.map">maze-128-128-10.map</option>
</select>
<label>Algorithm</label><select id="algorithm">
  <option value="AStar">AStar</option>
  <option value="PIBT">PIBT</option>
</select>
<label>Max players</label><input id="maxPlayers" type="number" min="1" max="8" value="8">
<button id="submit">Create room</button>
<pre id="out"></pre>
<script>
const out = document.getElementById('out');
document.getElementById('submit').onclick = async () => {
  out.textContent = 'Creating room...';
  const body = {
    map: document.getElementById('map').value,
    algorithm: document.getElementById('algorithm').value,
    maxPlayers: parseInt(document.getElementById('maxPlayers').value || '8', 10),
  };
  const resp = await fetch('/api/rooms', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  });
  const data = await resp.json().catch(() => ({}));
  out.textContent = JSON.stringify(data, null, 2);
};
</script></body></html>"""
        self.write_html(200, html)

    def build_session(self, body):
        code = str(body.get("code") or "").strip().upper()
        if not code:
            code = secrets.token_hex(3).upper()

        host = str(body.get("host") or "").strip()
        if not host:
            raise ValueError("host is required")

        try:
            game_port = int(body.get("gamePort"))
        except (TypeError, ValueError):
            raise ValueError("gamePort must be an integer")

        if game_port <= 0 or game_port > 65535:
            raise ValueError("gamePort must be between 1 and 65535")

        map_name = str(body.get("map") or "").strip()
        if not map_name:
            raise ValueError("map is required")

        algorithm = str(body.get("algorithm") or "").strip()
        if not algorithm:
            raise ValueError("algorithm is required")

        transport = str(body.get("transport") or "udp").strip().lower()
        if transport not in {"udp", "websocket"}:
            raise ValueError("transport must be udp or websocket")

        try:
            max_players = int(body.get("maxPlayers"))
        except (TypeError, ValueError):
            raise ValueError("maxPlayers must be an integer")

        if max_players <= 0:
            raise ValueError("maxPlayers must be greater than 0")

        expires_in_seconds = body.get("expiresInSeconds", 7200)
        try:
            expires_in_seconds = int(expires_in_seconds)
        except (TypeError, ValueError):
            raise ValueError("expiresInSeconds must be an integer")

        if expires_in_seconds <= 0:
            raise ValueError("expiresInSeconds must be greater than 0")

        web_host = str(body.get("webHost") or "").strip()
        web_transport = str(body.get("webTransport") or "").strip().lower()
        if web_transport and web_transport not in {"udp", "websocket"}:
            raise ValueError("webTransport must be udp or websocket")

        web_game_port = body.get("webGamePort")
        if web_game_port in (None, ""):
            web_game_port = 0
        else:
            try:
                web_game_port = int(web_game_port)
            except (TypeError, ValueError):
                raise ValueError("webGamePort must be an integer")
            if web_game_port <= 0 or web_game_port > 65535:
                raise ValueError("webGamePort must be between 1 and 65535")

        web_url = str(body.get("webUrl") or "").strip()

        now = int(time.time())
        session = {
            "code": code,
            "host": host,
            "gamePort": game_port,
            "transport": transport,
            "map": map_name,
            "algorithm": algorithm,
            "maxPlayers": max_players,
            "expiresAt": now + expires_in_seconds,
            "createdAt": now,
            "joinUrl": build_join_url(code),
        }
        if web_host:
            session["webHost"] = web_host
        if web_game_port:
            session["webGamePort"] = web_game_port
        if web_transport:
            session["webTransport"] = web_transport
        if web_url:
            session["webUrl"] = web_url
        return session

    def create_room(self, body):
        map_name = str(body.get("map") or "").strip()
        if not map_name:
            raise ValueError("map is required")

        algorithm = str(body.get("algorithm") or "").strip()
        if not algorithm:
            raise ValueError("algorithm is required")

        try:
            max_players = int(body.get("maxPlayers", 8))
        except (TypeError, ValueError):
            raise ValueError("maxPlayers must be an integer")
        if max_players <= 0:
            raise ValueError("maxPlayers must be greater than 0")

        session_code = str(body.get("code") or "").strip().upper() or secrets.token_hex(3).upper()
        if not os.path.exists(CREATE_ROOM_HELPER):
            raise ValueError(f"Create room helper not found: {CREATE_ROOM_HELPER}")

        try:
            subprocess.run(
                [
                    "sudo",
                    CREATE_ROOM_HELPER,
                    "--map", map_name,
                    "--algorithm", algorithm,
                    "--code", session_code,
                    "--max-players", str(max_players),
                ],
                check=True,
                capture_output=True,
                text=True,
                # The helper now blocks until the WebSocket server is ready (~6-10s),
                # with its own 45s internal cap. Bound the wait so a crash-looping
                # server cannot hang the HTTP request indefinitely.
                timeout=75,
            )
        except subprocess.TimeoutExpired as exc:
            output = (exc.stderr or exc.stdout or "").strip()
            raise ValueError(
                f"Create room timed out waiting for the WebSocket server: {output}"
            ) from exc
        except subprocess.CalledProcessError as exc:
            output = (exc.stderr or exc.stdout or str(exc)).strip()
            raise ValueError(f"Create room failed: {output}") from exc

        sessions = load_sessions()
        session = sessions.get(session_code)
        if session is None:
            raise ValueError("Create room helper completed but session was not published")
        return session

    def is_authorized(self):
        header = self.headers.get("Authorization") or ""
        return header == f"Bearer {REGISTRY_ADMIN_TOKEN}"

    def write_json(self, status_code, payload):
        raw = json.dumps(payload).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def write_html(self, status_code, html):
        raw = html.encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def write_redirect(self, status_code, location):
        self.send_response(status_code)
        self.send_header("Location", location)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, fmt, *args):
        print(f"[Registry] {self.address_string()} - {fmt % args}")


def main():
    ensure_data_dir()
    if not os.path.exists(REGISTRY_DATA_FILE):
        save_sessions({})

    server = ThreadingHTTPServer((REGISTRY_HOST, REGISTRY_PORT), InviteRegistryHandler)
    print(
        f"[Registry] Listening on http://{REGISTRY_HOST}:{REGISTRY_PORT} "
        f"(public base: {REGISTRY_PUBLIC_BASE_URL})"
    )
    print(f"[Registry] Data file: {REGISTRY_DATA_FILE}")
    server.serve_forever()


if __name__ == "__main__":
    main()
