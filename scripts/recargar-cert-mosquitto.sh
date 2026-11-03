#!/bin/sh
# Caddy renueva el certificado de Let's Encrypt cada ~60 días; Mosquitto lee una copia (iniciar.sh). Este script copia
# el certificado vigente y le manda SIGHUP al broker, que lo recarga sin cortar las conexiones de los wearables.
# Prográmalo con cron una vez por semana (docs/despliegue.md):
#   0 4 * * 1  /ruta/al/repo/scripts/recargar-cert-mosquitto.sh >> /var/log/vitalify-cert.log 2>&1
set -eu
cd "$(dirname "$0")/.."

docker compose -f docker-compose.prod.yml exec -T mosquitto sh /vitalify/copiar-cert.sh
docker compose -f docker-compose.prod.yml kill -s HUP mosquitto
echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) Mosquitto recargó el certificado."
