import copy
import hashlib
from pathlib import Path
import threading
import unittest
import uuid

from fastapi.testclient import TestClient
from server.app import create_app, ROM_SHA1, RoomCreate, fingerprint

ROOT = Path(__file__).resolve().parents[1]


def room():
    return {"name": "Integration room", "host": "Agent", "map": "Facility", "mode": "deathmatch", "capacity": 4,
            "build": {"engine_commit": "a" * 40, "engine_sha256": "b" * 64, "rom_sha1": ROM_SHA1, "transport": "none", "mods": []}}


class DirectoryTests(unittest.TestCase):
    def setUp(self):
        self.now = 1000000.0
        self.path = ROOT / ".local" / "server-tests" / uuid.uuid4().hex / "rooms.sqlite3"
        self.app = create_app(self.path, clock=lambda: self.now)
        self.client = TestClient(self.app)
        self.client.__enter__()

    def tearDown(self):
        self.client.__exit__(None, None, None)

    def create(self, data=None):
        response = self.client.post("/v1/rooms", json=data or room())
        self.assertEqual(response.status_code, 201, response.text)
        data = response.json()
        return data["room"]["id"], {"Authorization": "Bearer " + data["host_token"]}, data

    def test_full_lifecycle_and_secret_exclusion(self):
        identifier, headers, created = self.create()
        listed = self.client.get("/v1/rooms").json()
        self.assertEqual(listed["rooms"][0]["id"], identifier)
        self.assertNotIn("host_token", str(listed))
        self.assertNotIn(created["host_token"], str(listed))
        self.assertNotIn("owner", str(listed))
        self.assertFalse(created["room"]["joinable"])
        self.assertFalse(self.client.get("/v1/info").json()["game_networking"])
        self.assertEqual(self.client.get(f"/v1/rooms/{identifier}/join").status_code, 409)
        self.now += 30
        self.assertEqual(self.client.post(f"/v1/rooms/{identifier}/heartbeat", headers=headers, json={}).status_code, 200)
        self.now += 30
        self.assertEqual(len(self.client.get("/v1/rooms").json()["rooms"]), 1)
        self.assertEqual(self.client.delete(f"/v1/rooms/{identifier}", headers=headers).status_code, 200)
        self.assertEqual(self.client.get("/v1/rooms").json()["rooms"], [])

    def test_expiry_cannot_be_resurrected(self):
        identifier, headers, _ = self.create()
        self.now += 45
        self.assertEqual(self.client.post(f"/v1/rooms/{identifier}/heartbeat", headers=headers, json={}).status_code, 404)
        self.assertEqual(self.client.get("/v1/rooms").json()["rooms"], [])

    def test_forged_tokens_do_not_mutate(self):
        identifier, headers, _ = self.create()
        for auth in [{}, {"Authorization": "Bearer " + "x" * 43}]:
            self.assertEqual(self.client.delete(f"/v1/rooms/{identifier}", headers=auth).status_code, 401)
            self.assertEqual(self.client.post(f"/v1/rooms/{identifier}/heartbeat", headers=auth, json={}).status_code, 401)
        self.assertEqual(len(self.client.get("/v1/rooms").json()["rooms"]), 1)

    def test_strict_validation_and_no_reflection(self):
        variants = [{**room(), "name": "<script>alert(1)</script>"}, {**room(), "capacity": "4"},
                    {**room(), "capacity": 9}, {**room(), "password": "private-value"}, {**room(), "name": " \n "}]
        bad_transport = room(); bad_transport["build"]["transport"] = "enet"; variants.append(bad_transport)
        for data in variants:
            response = self.client.post("/v1/rooms", json=data)
            self.assertEqual(response.status_code, 422, response.text)
            self.assertNotIn("private-value", response.text)
        self.assertEqual(self.client.post("/v1/rooms", content=b"{}", headers={"Content-Type": "text/plain"}).status_code, 415)
        self.assertEqual(self.client.post("/v1/rooms", content=b" " * 9000, headers={"Content-Type": "application/json"}).status_code, 413)

    def test_filters_and_mod_identity(self):
        _, _, created = self.create()
        self.assertEqual(len(self.client.get("/v1/rooms?build=" + "b" * 64).json()["rooms"]), 1)
        self.assertEqual(self.client.get("/v1/rooms?build=" + "c" * 64).json()["rooms"], [])
        value = room(); original = fingerprint(RoomCreate(**value))
        value["build"]["mods"] = [{"id": "visual", "sha256": "d" * 64}]
        self.assertNotEqual(original, fingerprint(RoomCreate(**value)))
        value["build"]["mods"].append({"id": "map", "sha256": "e" * 64})
        a = fingerprint(RoomCreate(**value)); value["build"]["mods"].reverse()
        self.assertEqual(a, fingerprint(RoomCreate(**value)))

    def test_create_rate_limit_and_recovery(self):
        for i in range(3): self.create()
        self.assertEqual(self.client.post("/v1/rooms", json=room()).status_code, 429)
        self.now += 61
        self.create()

    def test_database_reopen_preserves_lease(self):
        identifier, headers, _ = self.create()
        with TestClient(create_app(self.path, clock=lambda: self.now)) as second:
            self.assertEqual(second.get("/v1/rooms").json()["rooms"][0]["id"], identifier)
            self.assertEqual(second.delete(f"/v1/rooms/{identifier}", headers=headers).status_code, 200)

    def test_http_headers_and_static_routes(self):
        for route in ["/", "/style.css", "/rooms.js", "/healthz", "/v1/info", "/v1/rooms"]:
            response = self.client.get(route)
            self.assertEqual(response.status_code, 200, route)
            self.assertEqual(response.headers["cache-control"], "no-store")
            self.assertEqual(response.headers["x-content-type-options"], "nosniff")
        self.assertEqual(self.client.get("/.env").status_code, 404)
        self.assertEqual(self.client.get("/rooms.sqlite3").status_code, 404)

    def test_cloudflare_mode_requires_valid_ip(self):
        with TestClient(create_app(self.path, trust_cloudflare=True)) as remote:
            self.assertEqual(remote.get("/healthz").status_code, 200)
            self.assertEqual(remote.get("/").status_code, 200)
            self.assertEqual(remote.get("/v1/rooms").status_code, 400)
            self.assertEqual(remote.get("/v1/rooms", headers={"CF-Connecting-IP": "not-an-ip"}).status_code, 400)
            self.assertEqual(remote.get("/v1/rooms", headers={"CF-Connecting-IP": "203.0.113.2"}).status_code, 200)

    def test_chunked_body_is_bounded(self):
        def chunks():
            yield b" " * 4096
            yield b" " * 4097
        response = self.client.post("/v1/rooms", content=chunks(), headers={"Content-Type": "application/json"})
        self.assertEqual(response.status_code, 413)

    def test_network_limits_cannot_be_reset_by_closing_rooms(self):
        for _ in range(3):
            identifier, headers, _ = self.create()
            self.client.delete(f"/v1/rooms/{identifier}", headers=headers)
        self.assertEqual(self.client.post("/v1/rooms", json=room()).status_code, 429)


if __name__ == "__main__":
    unittest.main()
