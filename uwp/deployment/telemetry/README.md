# Self-hosted Horizon telemetry

This deployment runs Horizon's upstream telemetry receiver on the BytesDirect Oracle host at
`https://horizontelemetry.bytesdirect.com`. The image builds the official server from the pinned
Horizon revision `f42ef42ef6ceccb3c6a20bea2481d87949a51c81`, matching the current MCHorizonUWP
catalog's Horizon base; only the root Cargo manifest and the two telemetry crates are fetched into
the build. No Minecraft game files or user data are needed.

The container has no published host port. It shares the existing proxy network with Nginx, runs as
UID/GID 10001 with a read-only root filesystem, drops all Linux capabilities, and has 256 MiB memory,
0.5 CPU and 64 process limits. SQLite lives under `/srv/horizontelemetry/data`. Nginx forwards the
real remote address only for the server's in-memory rate limiter. Nginx access logging is disabled
and error logs for this hostname go to `/dev/null`; the server does not persist client IPs. Raw
telemetry rows expire after 180 days.

No GitHub token is configured, so the server's optional `/hub/report` endpoint remains unavailable.
No export token is configured, so the owner-only `/v1/export/*` routes stay disabled. Native
Horizon clients can use the telemetry and benchmark endpoints without those features.

## Install or update the service

On the Oracle host, copy this directory to `/opt/horizontelemetry` and create the persistent data
directory with the runtime UID:

```sh
sudo install -d -o 10001 -g 10001 -m 0700 /srv/horizontelemetry/data
cd /opt/horizontelemetry
sudo docker compose -f compose.yaml config --quiet
sudo docker compose -f compose.yaml up -d --build
sudo docker compose -f compose.yaml ps
```

The Docker build pulls the pinned upstream revision directly from GitHub and compiles for ARM64.
To update Horizon's receiver, change the commit in both `Dockerfile`'s default `HORIZON_REVISION`
and `compose.yaml`'s build argument, image tag and revision label, then rebuild. The image label
`org.opencontainers.image.revision` records the exact Horizon source SHA.

## DNS and TLS

Create both DNS records for `horizontelemetry.bytesdirect.com`:

| Type | Name | Value |
|---|---|---|
| A | `horizontelemetry` | `144.21.57.120` |
| AAAA | `horizontelemetry` | `2603:c020:c00b:9500:0:84de:e613:501` |

These records now resolve to the Oracle instance. Host metadata confirms the IPv6 address is
assigned to its VNIC, but exposes no OCI lifetime field; Linux reports a dynamic DHCPv6 lease. The
host's external IPv4 is also visible, but its OCI `PublicIp` object type is not exposed. Verify
whether each address is ephemeral or reserved in the OCI Console/API if it must survive replacing
the VNIC or instance.

Back up the existing proxy configuration. Install `nginx-bootstrap.conf` as
`/opt/bytesdirect-secrets/nginx/horizontelemetry.conf` and add this include once to the end of
`/opt/bytesdirect-secrets/nginx/resolver-auth.conf`:

```nginx
include /etc/nginx/secrets/horizontelemetry.conf;
```

Run `sudo docker exec central_nginx_proxy nginx -t` and gracefully reload only
`central_nginx_proxy`. The bootstrap vhost serves HTTP-01 challenges and returns 404 for all other
HTTP paths; it deliberately does not send telemetry over cleartext HTTP.

With both records resolving as above, issue the certificate using the existing hardened Certbot
image and shared certificate/webroot volumes:

```sh
sudo docker run --rm --user 101:101 --read-only --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --tmpfs /tmp:rw,nosuid,nodev,size=32m,uid=101,gid=101 \
  --tmpfs /var/lib/letsencrypt:rw,nosuid,nodev,size=32m,uid=101,gid=101 \
  --tmpfs /var/log/letsencrypt:rw,nosuid,nodev,size=16m,uid=101,gid=101 \
  -v reverse-proxy_letsencrypt_certs_vol:/etc/letsencrypt \
  -v reverse-proxy_certbot_webroot_vol:/var/www/certbot \
  local/certbot:5.8.0-security-20261026 certonly --webroot \
  -w /var/www/certbot -d horizontelemetry.bytesdirect.com \
  --non-interactive --agree-tos --register-unsafely-without-email
```

Then install `nginx.conf` as `/opt/bytesdirect-secrets/nginx/horizontelemetry.conf`, run `nginx -t`
and gracefully reload only `central_nginx_proxy`. Keep the previous route and include backups for
rollback. Do not recreate or restart the broader Docker stack.

Install the renewal script, systemd service and timer:

```sh
sudo install -o root -g root -m 0750 horizontelemetry-cert-renew.sh /usr/local/sbin/
sudo install -o root -g root -m 0644 horizontelemetry-cert-renew.service /etc/systemd/system/
sudo install -o root -g root -m 0644 horizontelemetry-cert-renew.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now horizontelemetry-cert-renew.timer
sudo systemctl list-timers horizontelemetry-cert-renew.timer
```

The timer checks twice daily, renews only this certificate through the existing hardened Certbot
image, then validates and reloads the existing proxy. Test renewal with
`sudo docker compose -f /opt/horizontelemetry/renew.compose.yaml run --rm --no-deps certbot renew --dry-run --cert-name horizontelemetry.bytesdirect.com`.

Verify `https://horizontelemetry.bytesdirect.com/healthz` returns `ok` over IPv4 and IPv6 with
normal TLS certificate validation. Check `/v1/aggregate?days=30` and `/v1/benchmarks/summary`
return JSON. The server accepts batches at `POST /v1/batch` and exposes the same
`DELETE /v1/installs/<id>` path as the official receiver.

## Data and privacy

The server applies Horizon's upstream caps and retention rules. IP addresses are used only in
memory for rate limiting; this Nginx vhost disables access logs, and forwards a single trusted
`X-Real-IP` value for that limiter. The bound SQLite directory must remain private to the service
UID. Back it up using a SQLite-consistent backup operation and keep backups access-controlled.
