# Proposed lobby API v1 — not implemented

Future game-lobby origin: `https://lobbies.canadaverse.org`. Paths and values below are design contracts, not implemented endpoints. The separate staging-room service is implemented in `server/app.py`; see [room-api.md](room-api.md). Production deployment remains pending.

| Method/path | Purpose | Authorization |
| --- | --- | --- |
| `POST /v1/lobbies` | Register a match, return opaque lobby ID and 45-second lease | Rate-limited host session; server issues lease secret |
| `PUT /v1/lobbies/{id}/heartbeat` | Refresh lease and player count every 15 seconds | Host lease bearer secret |
| `DELETE /v1/lobbies/{id}` | Close a match | Host lease bearer secret |
| `GET /v1/lobbies?fingerprint=...` | List compatible public lobbies, bounded page size | Public, rate limited |
| `POST /v1/lobbies/{id}/join` | Check compatibility/capacity; return a short-lived signed join ticket | Client session or private invite code |
| `WSS /v1/signaling` | Exchange ICE candidates and connection status for authorized peers | Ticket tied to lobby, peer, role and expiry |

A lobby stores a short display name, region hint, current/capacity counts, map/rules identifiers, simulation fingerprint, supported protocol, transport capabilities and lease expiration. Public listings need not expose private LAN addresses. Host-reported public addresses must be verified by connection tests; never fetch arbitrary URLs supplied by a host. Escape display text in every client.

The join ticket binds a random session ID, host/client peer IDs, protocol, fingerprint, issued/expiry timestamps and a nonce. It authorizes connection, not arbitrary relay use. Relay credentials are scoped, short-lived and issued separately. Reject expired/replayed tickets, incompatible versions and over-capacity sessions. Never log bearer tokens, invite codes or relay passwords.

The game protocol requires separate message definitions for HELLO/ACCEPT/REJECT, READY/START, INPUT, SNAPSHOT, SPAWN/DESPAWN, authoritative events, time synchronization and disconnect. Each decoder must validate length, count, sequence, tick range, ownership and state transitions. The prototype compatibility helper is not a substitute for these messages.

No API endpoint accepts ROM uploads, extracted assets, local file paths, arbitrary executable commands or downloadable mod scripts. Future mod discovery should return reviewed content-addressed manifests; installation remains an explicit launcher operation with hash/signature checks.
