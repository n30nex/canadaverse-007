# Multiplayer implementation plan

## Actual status

The pinned engine supports local split-screen multiplayer, per-pad input, local match rules, profiles and statistics. Inspection of `CMakeLists.txt`, `port/` and `src/game/` found no game transport, network session handshake, entity replication, remote input channel, lobby discovery, NAT traversal or relay integration. `port/src/port_console.c` explicitly documents that there is no netplay. Mentions of ENet elsewhere in project acknowledgements are not a linked networking implementation. The README's co-op/netplay roadmap is not a shipped feature.

The launcher and `server/app.py` now implement staging-room discovery, build filters and expiring host leases; see `protocol/room-api.md`. The actual game transport and the future lobby/signaling protocol below remain unimplemented. **A room directory cannot synchronize this game.** Pi/Cloudflare deployment is prepared but not yet executed.

## Architecture to build

Use a host-authoritative match first, with the option to run the same simulation as a dedicated server later. Avoid beginning with rollback/lockstep: pointers, allocator layout, wall-clock/sub-field deltas, global state and RNG need substantial determinism work before either is safe. Local split-screen code provides useful player slots but is not a remote-state serializer.

| Component | Concrete responsibility |
| --- | --- |
| Engine session layer | Fixed simulation cadence, stable entity IDs, player-slot ownership, input sequence/tick numbers, join/leave and match lifecycle |
| Host simulation | Authoritative movement/collision, weapon rate/ammo, hits/damage, pickups, doors, projectiles, respawns, score and RNG |
| Client presentation | Predict local input, reconcile acknowledged input, interpolate remote snapshots, deduplicate events, render independently of server tick |
| Game transport | Reliable ordered control/events plus sequenced unreliable input/snapshots, disconnect timeouts, bounded message sizes, congestion behavior |
| Lobby service | Register/list/expire lobbies and issue short-lived join tickets; never tick the game or accept ROM/assets |
| NAT/relay layer | Establish a reachable game packet path and maintain it; separate from lobby HTTP traffic |
| Launcher | Select an engine/mod set, validate the user's ROM locally, match compatibility, start/join via a future documented engine API |

The first network prototype should run **fixed 60 Hz simulation**. The current 144 Hz mode integrates fractional motion at a variable measured cadence; do not replicate frame counts or assume replay determinism. Implement/render interpolation separately before allowing clients to choose independent rendering rates. Keep the current single-player graphics settings untouched.

## Milestones with exit criteria

1. **Simulation seam and replay baseline.** Introduce `port/net/session.*`, a per-tick input structure, stable ID tables and a serializable match state. Route `port/src/input_pc.c`, `joy_port.c`, `libultra_shim.c` and game player updates through explicit local/remote slots. Instrument `src/game/frametiming.c`, `port/src/pcsched.c`, `src/boss.c`, multiplayer rules and RNG ownership. Record positions, health, ammo, door states and score for a fixed input trace. Exit: two local runs produce the same authoritative state hash or every nondeterministic field has a documented treatment.
2. **Direct-IP LAN game.** Two separate processes with independent cameras; host owns all game outcomes. Candidate transport: ENet, which supplies reliable/sequenced UDP channels, not game replication or encryption. Send input around 60 Hz and start with snapshots at 20 Hz; tune from measurements. Channel 0 handles reliable session/rules/spawns, channel 1 sequenced snapshots, channel 2 sequenced inputs. Include tick IDs and last acknowledged input. Do not send C structs, pointers or raw memory; use explicit endian-safe fields and bounded decoding. Exit: a complete two-player match, correct deaths/score/pickups/doors, disconnect handling, and 30-minute soak with artificial loss/reordering/latency.
3. **Internet-safe transport and compatibility.** Add authenticated connection tickets, replay protection, parser fuzzing and an authenticated encryption layer using a reviewed library/protocol rather than home-grown crypto. Lock compatibility before allocating player/world state. Host validates movement speed, firing cadence and inventory instead of trusting a client hit claim. Exit: malformed packets are rejected, incompatible builds cannot join, reconnect does not duplicate a player, 100–150 ms RTT and 1–5% packet loss remain playable.
4. **Game-ready lobby discovery.** The current staging directory implements room listings and host leases. Extend it with the join-ticket and signaling contract in `protocol/lobby-api.md`, initially locally. Proposed later origin: `https://lobbies.canadaverse.org`. Register hosts, refresh a lease every 15 seconds, expire after 45 seconds, and filter by simulation fingerprint. The service returns tickets/candidates, not ROMs. Use HTTPS, bounded payloads, server-issued host leases, rate limits and explicit private/public visibility. Exit: stale hosts vanish, spoofed renewals fail, version filtering and ticket expiry are tested.
5. **NAT traversal and relay.** Integrate ICE/STUN candidate gathering and connectivity checks into the client and host. Exchange candidates through authenticated signaling. Prefer direct paths, but support an authenticated TURN relay for symmetric NAT, CGNAT or restrictive firewalls. A TURN server alone does not make ENet TURN-aware: implement/test a datagram transport adapter or choose a transport stack that already integrates ICE/relay. Proposed later `relay.canadaverse.org` requires public reachability, UDP listener/allocation ports, short-lived credentials, allocation limits and bandwidth quotas. Test LAN hairpinning, two separate home networks, mobile CGNAT, IPv4/IPv6 and forced relay. TCP/TLS relay fallback is a separate latency test for UDP-blocked networks, not a guarantee of equal play quality.
6. **Broader game modes and delivery.** Expand from one competitive arena to all supported maps, then investigate co-op mission AI/objectives separately. Add signed engine/mod manifests, transactional installation, hashes, update rollback and independent save profiles. Dedicated servers and host migration are later features; the first release may end a match when its host leaves, with a clear reason.

## Version matching

The handshake and lobby record must include protocol version, exact engine commit, build/patch identity, stock ROM identity, fixed simulation tick rate, map/rules digest and sorted simulation-mod IDs with SHA256 hashes. A displayed semantic version alone is insufficient. The server and engine both enforce the same fingerprint; a client cannot evade the rule by altering its lobby query. For distributed binaries, use a signed build manifest so a claimed commit is tied to the shipped executable.

Visual-only mods may differ only after the engine enforces that they cannot affect collision, visibility rules, entity logic, hitboxes or timing. An unclassified mod is simulation-affecting. `protocol/compatibility.py` demonstrates canonical hash construction and tests the distinction; it is not connected to any engine handshake yet. Never transmit the user's ROM or extracted assets to perform these checks.

## Infrastructure and bandwidth

The lobby service can run separately from the relay and future dedicated simulation workers. A relay has recurring bandwidth costs even if lobby traffic is tiny. As a sizing example, four clients each receiving 50 KiB/s for one hour imply about 703 MiB of outbound relay traffic before headers/retransmissions; measure the real snapshot format before choosing capacity. Store service secrets only in deployment secret storage, not this repository. Do not copy the user's ROM to a public server; a hosted game simulation would require a separate asset/licensing decision.

Primary references: [ENet upstream](https://github.com/lsalzman/enet), [ENet channel/reliability design](https://github.com/lsalzman/enet/blob/master/docs/design.dox), [ICE RFC 8445](https://www.rfc-editor.org/rfc/rfc8445.html), [TURN RFC 8656](https://www.rfc-editor.org/rfc/rfc8656.html). These describe transport mechanisms. All game architecture and milestone choices above are a proposed design, not features present in Project Midas.
