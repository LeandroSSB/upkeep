#!/usr/bin/env bash
# Deploy canônico do upkeep (no megalan: ~/upkeep). O comando é ATÔMICO:
# --env-file e -p upkeep JAMAIS podem ser omitidos (sem env-file a interpolação
# vazia derruba a API em crash-loop no guard do Jwt:Key — incidente 2026-10-03).
set -euo pipefail
cd "$(dirname "$0")"

git pull --ff-only
docker compose -f docker-compose.yml -f docker-compose.prod.yml \
  -p upkeep --env-file .env.production up -d --build

echo "aguardando health..."
for i in $(seq 1 30); do
  if curl -sf -o /dev/null http://127.0.0.1:14010/health; then
    echo "OK — upkeep no ar ($(git log --oneline -1))"
    exit 0
  fi
  sleep 2
done
echo "FALHOU: health não respondeu em 60s — verifique: docker logs upkeep-api-1" >&2
exit 1
