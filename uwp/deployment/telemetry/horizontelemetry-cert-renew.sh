#!/bin/sh
set -eu

docker compose \
  --project-directory /opt/horizontelemetry \
  -f /opt/horizontelemetry/renew.compose.yaml \
  run --rm --no-deps certbot

docker exec central_nginx_proxy nginx -t
docker exec central_nginx_proxy nginx -s reload
