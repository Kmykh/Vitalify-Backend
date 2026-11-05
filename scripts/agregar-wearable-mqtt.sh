#!/bin/sh
# Crea (o cambia) la clave MQTT de un wearable en el broker de producción y lo recarga (SIGHUP, sin cortar conexiones).
# El usuario es el código del dispositivo (por ejemplo ESP32-005); la clave se escribe dos veces y no queda en el
# historial de la terminal. Uso:  scripts/agregar-wearable-mqtt.sh ESP32-005
set -eu
codigo="${1:?Uso: scripts/agregar-wearable-mqtt.sh CODIGO-DEL-DISPOSITIVO}"
cd "$(dirname "$0")/.."

docker compose -f docker-compose.prod.yml exec -u mosquitto mosquitto mosquitto_passwd /mosquitto/secretos/passwd "$codigo"
docker compose -f docker-compose.prod.yml kill -s HUP mosquitto
echo "Listo: $codigo puede conectarse por el puerto 8883 con su clave."
