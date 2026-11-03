#!/bin/sh
# Copia el certificado vigente que Caddy obtuvo para $DOMINIO (volumen de Caddy, montado en solo lectura en /caddy) a
# /mosquitto/certs, con permisos para el usuario "mosquitto". Corre como root dentro del contenedor.
# Lo usan iniciar.sh al arrancar y scripts/recargar-cert-mosquitto.sh cuando Caddy renueva el certificado.
set -eu
: "${DOMINIO:?falta DOMINIO}"

# Caddy guarda <emisor>/<dominio>/<dominio>.crt y .key: Let's Encrypt, ZeroSSL (respaldo) o "local" para *.localhost.
# Si hay más de uno, el más reciente.
certificado=$(find /caddy/caddy/certificates -type f -path "*/$DOMINIO/$DOMINIO.crt" 2>/dev/null | xargs -r ls -t 2>/dev/null | head -n 1)
if [ -z "$certificado" ]; then
  exit 1
fi
clave="${certificado%.crt}.key"

mkdir -p /mosquitto/certs
cp "$certificado" /mosquitto/certs/certificado.pem
cp "$clave" /mosquitto/certs/clave.pem
chown -R mosquitto:mosquitto /mosquitto/certs
chmod 0644 /mosquitto/certs/certificado.pem
chmod 0600 /mosquitto/certs/clave.pem
echo "Certificado de $DOMINIO copiado desde $(dirname "$certificado")"
