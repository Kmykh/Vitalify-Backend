#!/bin/sh
# Punto de entrada del Mosquitto de producción (corre como root; Mosquitto suelta los privilegios al arrancar):
#   1. espera a que Caddy tenga el certificado de $DOMINIO y lo copia (copiar-cert.sh);
#   2. copia la configuración y el ACL con permisos solo para el usuario "mosquitto";
#   3. exige el archivo de claves (deploy/mosquitto/secretos/passwd, creado con mosquitto_passwd; nunca va a git);
#   4. arranca Mosquitto.
set -eu
: "${DOMINIO:?falta DOMINIO en el .env}"

intentos=0
until sh /vitalify/copiar-cert.sh; do
  intentos=$((intentos + 1))
  if [ "$intentos" -ge 60 ]; then
    echo "Caddy no tiene todavía el certificado de $DOMINIO. ¿El dominio apunta al servidor y los puertos 80 y 443 están abiertos?" >&2
    exit 1
  fi
  [ $((intentos % 6)) -eq 1 ] && echo "Esperando el certificado de $DOMINIO que obtiene Caddy..."
  sleep 5
done

mkdir -p /mosquitto/config
cp /vitalify/mosquitto.conf /mosquitto/config/mosquitto.conf
cp /vitalify/acl /mosquitto/config/acl

claves=/mosquitto/secretos/passwd
if [ ! -s "$claves" ]; then
  echo "Falta $claves. Créalo con mosquitto_passwd (docs/despliegue.md, paso 5)." >&2
  exit 1
fi

# Mosquitto exige que el ACL y las claves no sean legibles por otros usuarios. El directorio de secretos queda del
# usuario "mosquitto" para que mosquitto_passwd (con -u mosquitto) pueda reescribir el archivo.
chown mosquitto:mosquitto /mosquitto/config/acl "$claves" /mosquitto/secretos
chmod 0600 /mosquitto/config/acl "$claves"
chown -R mosquitto:mosquitto /mosquitto/data

exec /usr/sbin/mosquitto -c /mosquitto/config/mosquitto.conf
