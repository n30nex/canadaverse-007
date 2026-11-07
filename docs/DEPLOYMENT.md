# Pi deployment and Cloudflare Tunnel

The directory is implemented and locally testable. Public deployment has not been completed because this session cannot execute SSH on the Pi. These files are a prepared deployment, not evidence of a running Internet service.

Requirements: Raspberry Pi OS/Debian with systemd, Python 3.10+, `python3-venv`, outbound package access, and the existing authorized Cloudflare Tunnel connector running on the same host. No Docker, ROM or game engine is required.

## Install the service

On the Pi, unpack the service archive (or check out this repository), inspect the deployment script, then run:

```bash
sudo bash deploy/install-pi.sh
```

The installer creates a versioned release below `/opt/canadaverse-007/releases`, builds an isolated Python environment, updates `current`, and starts `canadaverse-007.service`. It verifies localhost health and restores the previous release link if the new service fails its health check. Initial install requires `python3-venv` already installed. Existing unrelated services and tunnel configuration are not modified.

The service binds **127.0.0.1:18707**, runs with systemd's dynamic unprivileged user, uses one worker, and keeps SQLite data in `/var/lib/canadaverse-007`. Uvicorn forwarded-header parsing and access logs are disabled. `CF-Connecting-IP` is trusted only because the origin is loopback and reached through the tunnel.

```bash
systemctl status canadaverse-007 --no-pager
journalctl -u canadaverse-007 -n 50 --no-pager
curl --fail http://127.0.0.1:18707/healthz
curl --fail -H 'CF-Connecting-IP: 127.0.0.1' http://127.0.0.1:18707/v1/info
```

## Add the existing tunnel route

After localhost health passes, open Cloudflare **Networking > Tunnels**, select the existing Canadaverse Pi tunnel, then **Routes > Add route > Published application**:

| Field | Value |
| --- | --- |
| Hostname | `lobbies.canadaverse.org` |
| Service | `http://127.0.0.1:18707` |

Preserve every existing tunnel route and catch-all rule. No new tunnel credential, router port-forward, firewall opening, or disabled TLS validation is needed. Do not expose the origin listener on a LAN/public interface while `TRUST_CLOUDFLARE=1`. A remotely managed tunnel's dashboard normally creates the corresponding DNS route.

Official reference: [Add a published application route](https://developers.cloudflare.com/cloudflare-one/networks/routes/add-routes/).

## Verify public behavior

Check HTTPS `/healthz`, `/v1/info`, and the browser page. Then point two launchers at the HTTPS origin: create a staging room in one, observe it in the other, confirm a forged token is rejected, close it, and confirm removal. Stop heartbeats and verify expiry after 45 seconds. Check both independent networks before calling deployment complete.

Update the README's deployment status only after those checks pass. Real online game sessions remain unavailable even with a healthy directory.

## Roll back

Stop the service, point `/opt/canadaverse-007/current` to the previously printed release directory, and restart. Keep the SQLite state directory. Remove or disable only the newly added tunnel hostname if public routing needs to be withdrawn; leave other Canadaverse applications intact.
