"""Bounded, leased room discovery. The current engine has no online transport."""
from collections import OrderedDict, deque
from contextlib import asynccontextmanager
import hashlib
import hmac
import ipaddress
import json
import os
from pathlib import Path
import secrets
import sqlite3
import threading
import time
from typing import Annotated, Literal

from fastapi import FastAPI, Header, HTTPException, Query, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import FileResponse, JSONResponse
from pydantic import BaseModel, ConfigDict, Field, field_validator

VERSION = "0.2.0-preview.1"
LEASE_SECONDS = 45
HEARTBEAT_SECONDS = 15
ROM_SHA1 = "abe01e4aeb033b6c0836819f549c791b26cfde83"
STATIC = Path(__file__).parent / "static"
Hex40 = Annotated[str, Field(pattern=r"^[0-9a-f]{40}$")]
Hex64 = Annotated[str, Field(pattern=r"^[0-9a-f]{64}$")]


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)


class Mod(StrictModel):
    id: Annotated[str, Field(pattern=r"^[a-z0-9][a-z0-9._-]{0,63}$")]
    sha256: Hex64


class Build(StrictModel):
    engine_commit: Hex40
    engine_sha256: Hex64
    rom_sha1: Literal[ROM_SHA1]
    # Future game transport needs a new, explicitly supported protocol.
    transport: Literal["none"] = "none"
    mods: list[Mod] = Field(default_factory=list, max_length=32)

    @field_validator("mods")
    @classmethod
    def unique_mods(cls, value):
        if len({m.id for m in value}) != len(value):
            raise ValueError("Duplicate mod IDs")
        return value


class RoomCreate(StrictModel):
    name: str = Field(min_length=1, max_length=60)
    host: str = Field(min_length=1, max_length=24)
    map: Literal["Facility", "Complex", "Temple", "Library", "Caverns", "Archives", "Bunker", "Basement", "Stack"]
    mode: Literal["deathmatch", "golden-gun"] = "deathmatch"
    capacity: int = Field(default=4, ge=2, le=4)
    build: Build

    @field_validator("name", "host")
    @classmethod
    def display_text(cls, value):
        value = value.strip()
        if not value or any(not c.isprintable() or c in "<>" for c in value):
            raise ValueError("Use plain, printable text")
        return value


class Heartbeat(StrictModel):
    # This release has a host presence only. Do not invent connected players.
    status: Literal["staging"] = "staging"


def fingerprint(room: RoomCreate) -> str:
    """Directory compatibility, not a verified engine handshake or attestation."""
    build = room.build.model_dump()
    build["mods"] = sorted(build["mods"], key=lambda m: m["id"])
    data = {"schema": 1, "build": build, "map": room.map, "mode": room.mode}
    return hashlib.sha256(json.dumps(data, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


class LimitBody:
    def __init__(self, app, limit=8192):
        self.app, self.limit = app, limit

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            return await self.app(scope, receive, send)
        data = bytearray()
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            data.extend(message.get("body", b""))
            if len(data) > self.limit:
                return await JSONResponse({"detail": "Request body exceeds 8192 bytes"}, 413)(scope, receive, send)
            if not message.get("more_body", False):
                break
        delivered = False

        async def replay():
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": bytes(data), "more_body": False}
            return await receive()

        await self.app(scope, replay, send)


class Store:
    def __init__(self, path, clock=time.time):
        self.clock = clock
        self.lock = threading.RLock()
        self.rates = OrderedDict()
        self.db = sqlite3.connect(path, check_same_thread=False, isolation_level=None)
        self.db.row_factory = sqlite3.Row
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.execute("PRAGMA busy_timeout=5000")
        self.db.executescript("""
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS rooms (
                id TEXT PRIMARY KEY, token_hash TEXT NOT NULL, owner TEXT NOT NULL,
                payload TEXT NOT NULL, fingerprint TEXT NOT NULL, expires REAL NOT NULL,
                created REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS expiry ON rooms(expires);
        """)
        self.db.execute("INSERT OR IGNORE INTO meta VALUES ('ip_salt', ?)", (secrets.token_hex(32),))
        self.salt = bytes.fromhex(self.db.execute("SELECT value FROM meta WHERE key='ip_salt'").fetchone()[0])

    def owner(self, ip):
        return hmac.new(self.salt, ip.encode(), hashlib.sha256).hexdigest()

    def limit(self, key, maximum=90, window=60):
        now = self.clock()
        with self.lock:
            history = self.rates.pop(key, deque())
            while history and history[0] <= now - window:
                history.popleft()
            self.rates[key] = history
            if len(self.rates) > 4096:
                self.rates.popitem(last=False)
            if len(history) >= maximum:
                raise HTTPException(429, "Too many requests. Try again later.", headers={"Retry-After": str(window)})
            history.append(now)

    def prune(self):
        self.db.execute("DELETE FROM rooms WHERE expires <= ?", (self.clock(),))

    def public(self, row):
        room = json.loads(row["payload"])
        return {**room, "id": row["id"], "fingerprint": row["fingerprint"],
                "status": "staging", "players": 1, "joinable": False,
                "reason": "Project Midas has no online game transport in this release.",
                "expires_at": int(row["expires"]), "created_at": int(row["created"])}

    def create(self, room, owner):
        self.limit("create:" + owner, 3, 60)
        self.limit("hour:" + owner, 20, 3600)
        with self.lock:
            self.db.execute("BEGIN IMMEDIATE")
            try:
                self.prune()
                if self.db.execute("SELECT count(*) FROM rooms").fetchone()[0] >= 200:
                    raise HTTPException(503, "Room directory is full")
                if self.db.execute("SELECT count(*) FROM rooms WHERE owner=?", (owner,)).fetchone()[0] >= 3:
                    raise HTTPException(429, "At most three live rooms per network")
                room_id, token = secrets.token_hex(16), secrets.token_urlsafe(32)
                now = self.clock()
                self.db.execute("INSERT INTO rooms VALUES (?, ?, ?, ?, ?, ?, ?)",
                    (room_id, hashlib.sha256(token.encode()).hexdigest(), owner,
                     room.model_dump_json(), fingerprint(room), now + LEASE_SECONDS, now))
                row = self.db.execute("SELECT * FROM rooms WHERE id=?", (room_id,)).fetchone()
                self.db.execute("COMMIT")
            except BaseException:
                self.db.execute("ROLLBACK")
                raise
        return {"room": self.public(row), "host_token": token,
                "lease_seconds": LEASE_SECONDS, "heartbeat_seconds": HEARTBEAT_SECONDS}

    def authenticate(self, room_id, token):
        self.prune()
        row = self.db.execute("SELECT * FROM rooms WHERE id=?", (room_id,)).fetchone()
        if row is None:
            raise HTTPException(404, "Room expired or does not exist")
        if not hmac.compare_digest(row["token_hash"], hashlib.sha256(token.encode()).hexdigest()):
            raise HTTPException(401, "Invalid host lease")
        return row


def create_app(db_path=None, clock=time.time, trust_cloudflare=False):
    @asynccontextmanager
    async def lifespan(app):
        path = Path(db_path or os.environ.get("ROOM_DB", ".local/rooms.sqlite3"))
        path.parent.mkdir(parents=True, exist_ok=True)
        app.state.store = Store(path, clock)
        yield
        app.state.store.db.close()

    app = FastAPI(title="Canadaverse room directory", version=VERSION, lifespan=lifespan,
                  docs_url=None, redoc_url=None, openapi_url=None)
    app.add_middleware(LimitBody)

    @app.exception_handler(RequestValidationError)
    async def validation_error(request, exc):
        # Avoid reflecting request contents, including accidentally submitted secrets.
        return JSONResponse({"detail": "Invalid room data", "fields": [".".join(map(str, e["loc"])) for e in exc.errors()]}, 422)

    @app.middleware("http")
    async def guard(request: Request, call_next):
        try:
            if request.method in {"POST", "PATCH"} and request.headers.get("content-type", "").split(";")[0] != "application/json":
                raise HTTPException(415, "Use application/json")
            ip = request.client.host if request.client else "unknown"
            # Enable only behind a tunnel-only origin; never blindly trust forwarded IPs.
            if trust_cloudflare and request.url.path.startswith("/v1/"):
                try:
                    ip = str(ipaddress.ip_address(request.headers.get("cf-connecting-ip", "")))
                except ValueError:
                    raise HTTPException(400, "Missing Cloudflare client address")
            request.state.owner = app.state.store.owner(ip)
            if request.url.path.startswith("/v1/"):
                app.state.store.limit("request:" + request.state.owner)
            response = await call_next(request)
        except HTTPException as error:
            response = JSONResponse({"detail": error.detail}, error.status_code, headers=error.headers)
        response.headers.update({"Cache-Control": "no-store", "X-Content-Type-Options": "nosniff",
            "Referrer-Policy": "no-referrer", "X-Frame-Options": "DENY",
            "Content-Security-Policy": "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"})
        return response

    @app.get("/healthz")
    def health():
        with app.state.store.lock:
            app.state.store.db.execute("SELECT 1").fetchone()
        return {"ok": True, "service": "canadaverse-007-directory", "version": VERSION}

    @app.get("/v1/info")
    def info():
        return {"schema": 1, "version": VERSION, "game_networking": False,
                "capabilities": ["room-discovery", "host-leases", "build-filtering"],
                "lease_seconds": LEASE_SECONDS, "heartbeat_seconds": HEARTBEAT_SECONDS}

    @app.get("/v1/rooms")
    def rooms(build: str | None = Query(default=None, pattern=r"^[0-9a-f]{64}$")):
        store = app.state.store
        with store.lock:
            store.prune()
            query = "SELECT * FROM rooms"
            args = ()
            if build:
                query += " WHERE json_extract(payload, '$.build.engine_sha256')=?"
                args = (build,)
            rows = store.db.execute(query + " ORDER BY created DESC LIMIT 200", args).fetchall()
            return {"schema": 1, "rooms": [store.public(row) for row in rows], "server_time": int(clock())}

    @app.post("/v1/rooms", status_code=201)
    def create(room: RoomCreate, request: Request):
        return app.state.store.create(room, request.state.owner)

    def bearer(value):
        if not value or not value.startswith("Bearer ") or not 30 <= len(value[7:]) <= 100:
            raise HTTPException(401, "A host lease is required")
        return value[7:]

    @app.post("/v1/rooms/{room_id}/heartbeat")
    def heartbeat(room_id: str, body: Heartbeat, authorization: str | None = Header(default=None)):
        store = app.state.store
        with store.lock:
            store.authenticate(room_id, bearer(authorization))
            store.db.execute("UPDATE rooms SET expires=? WHERE id=?", (clock() + LEASE_SECONDS, room_id))
        return {"expires_at": int(clock() + LEASE_SECONDS), "lease_seconds": LEASE_SECONDS}

    @app.delete("/v1/rooms/{room_id}")
    def close(room_id: str, authorization: str | None = Header(default=None)):
        store = app.state.store
        with store.lock:
            store.authenticate(room_id, bearer(authorization))
            store.db.execute("DELETE FROM rooms WHERE id=?", (room_id,))
        return {"closed": True}

    @app.get("/v1/rooms/{room_id}/join")
    def join(room_id: str):
        raise HTTPException(409, "Online game transport is not implemented. Room discovery does not start a multiplayer game.")

    @app.get("/")
    def index():
        return FileResponse(STATIC / "index.html")

    @app.get("/style.css")
    def style():
        return FileResponse(STATIC / "style.css", media_type="text/css")

    @app.get("/rooms.js")
    def javascript():
        return FileResponse(STATIC / "rooms.js", media_type="text/javascript")

    return app


app = create_app(trust_cloudflare=os.environ.get("TRUST_CLOUDFLARE", "0") == "1")
