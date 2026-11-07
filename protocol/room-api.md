# Implemented staging-room API, schema 1

The service in `server/app.py` implements this contract. Production hosting is separate. It does not implement the future game-lobby/signaling contract in `lobby-api.md`.

| Method/path | Result | Authorization |
| --- | --- | --- |
| `GET /healthz` | Service and database health | None |
| `GET /v1/info` | Version, capabilities, `game_networking: false` | None |
| `GET /v1/rooms` | Up to 200 unexpired public rooms | None |
| `GET /v1/rooms?build=<sha256>` | Exact executable-hash filter | None |
| `POST /v1/rooms` | Room, random ID, secret host token, 45-second lease | Rate limited |
| `POST /v1/rooms/{id}/heartbeat` | Renew live lease | `Authorization: Bearer <host_token>` |
| `DELETE /v1/rooms/{id}` | Close room | Host token |
| `GET /v1/rooms/{id}/join` | HTTP 409: game transport unavailable | None |

Create JSON:

```json
{
  "name": "Friday Facility",
  "host": "Agent",
  "map": "Facility",
  "mode": "deathmatch",
  "capacity": 4,
  "build": {
    "engine_commit": "9062f9f120b7f64d39e5db59500ed50e8e7a6686",
    "engine_sha256": "5d524f96ea81d3374e2151fa9cd66157576d7a7dac51d3dfd729a25903103ca7",
    "rom_sha1": "abe01e4aeb033b6c0836819f549c791b26cfde83",
    "transport": "none",
    "mods": []
  }
}
```

Heartbeat body is `{}` or `{"status":"staging"}`. Renew every 15 seconds. An expired token cannot resurrect a room; create a new room instead. Host tokens are returned only on creation and stored hashed on the server. Public records contain neither tokens nor client addresses. Only the host is counted (`players: 1`); this is presence, not a connected game player.

Names are bounded printable text, fields are strict, and request bodies are limited to 8 KiB including chunked requests. Per-network limits: 90 API requests/minute, three new rooms/minute, 20/hour, three concurrent rooms. There are at most 200 live rooms and 4,096 in-memory rate-limit buckets. The service is designed for one worker. Restarting clears rate-limit buckets; it preserves unexpired SQLite leases.

No endpoint accepts ROMs, assets, files, private endpoints or commands. Client-provided build hashes are not attestations. There is no account authentication, private-room password, join ticket, game packet relay or chat feature.

Behind Cloudflare Tunnel, `TRUST_CLOUDFLARE=1` requires a valid `CF-Connecting-IP` on API requests and uses a salted HMAC of it for rate limiting. The origin must listen only on loopback, reachable by the trusted local tunnel. Health/static routes do not require that header. Keep access logs off; do not log host tokens.
