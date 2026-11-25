#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
export COMPOSE_PROJECT_NAME=minierp-cloud-dev
env_file=deploy/environments/development.env
if [ ! -f "$env_file" ]; then
  umask 077
  printf 'POSTGRES_PASSWORD=%s\nOBJECT_STORAGE_ACCESS_KEY=%s\nOBJECT_STORAGE_SECRET_KEY=%s\n' "$(openssl rand -hex 24)" "$(openssl rand -hex 12)" "$(openssl rand -hex 24)" > "$env_file"
fi
node scripts/ci/prepare-identity.cjs "$env_file"
docker compose --env-file "$env_file" -f deploy/compose/ci.yaml -f deploy/compose/development.yaml up -d --build --wait --wait-timeout 240
printf 'Open the private forwarded port 13000. Stop the Codespace when finished.\n'
