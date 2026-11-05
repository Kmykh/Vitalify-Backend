# Despliegue en producción

La API, Caddy y Mosquitto corren con Docker en una sola VM Linux. La base de datos es Supabase y no corre en el servidor.

```
Internet ──80/443──> caddy (HTTPS de Let's Encrypt) ──api:8080──> api ──5432, SSL──> Supabase
wearables ──8883, TLS──> mosquitto <──mosquitto:1883 (red interna)── api
```

| Servicio | Qué hace | Puertos en el servidor |
|---|---|---|
| `caddy` | Saca y renueva solo el certificado de `{DOMINIO}`, redirige HTTP a HTTPS y hace de proxy hacia la API (también WebSockets de SignalR) | 80 y 443 |
| `api` | La imagen del `Dockerfile`, en `Production` | ninguno |
| `mosquitto` | El broker de los wearables (`eclipse-mosquitto:2.0.20`). Sin anónimos, con `password_file` y el ACL de `deploy/mosquitto/acl` | 8883 (TLS); el 1883 queda solo en la red interna |

**Solo se abren tres puertos: 80, 443 y 8883.** La API y el puerto 1883 de Mosquitto no se publican.

Archivos: [`docker-compose.prod.yml`](../docker-compose.prod.yml), [`deploy/caddy/Caddyfile`](../deploy/caddy/Caddyfile), [`deploy/mosquitto/`](../deploy/mosquitto) (configuración, ACL y scripts de arranque) y [`scripts/`](../scripts).

## Requisitos

- Una VM Linux (por ejemplo, Ubuntu 24.04) con IP pública, 1 GB de RAM y 10 GB de disco.
- Un dominio que apunte a esa IP. Un subdominio gratuito de [DuckDNS](https://www.duckdns.org) sirve (paso 3).
- El proyecto de Supabase con las migraciones ya aplicadas (paso 4).

## Paso a paso

### 1. Instalar Docker

```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER   # cierra la sesión SSH y vuelve a entrar para usar docker sin sudo
docker compose version          # debe responder v2.x
```

Abre solo los puertos necesarios. Con `ufw` (en Ubuntu):

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 8883/tcp
sudo ufw enable
```

Si la VM es de un proveedor en la nube, abre también esos puertos en su firewall o grupo de seguridad.

### 2. Clonar el repositorio

```bash
git clone https://github.com/Kmykh/Vitalify-Backend.git
cd Vitalify-Backend
```

### 3. Apuntar el dominio

Con DuckDNS:

1. Entra a <https://www.duckdns.org>, crea un subdominio (por ejemplo `vitalify`) y escribe la IP pública de la VM. El dominio queda como `vitalify.duckdns.org`.
2. Si la IP de la VM puede cambiar, actualízala cada 5 minutos con cron. El token es el de tu cuenta de DuckDNS y no va al repositorio:
   ```bash
   crontab -e
   */5 * * * * curl -fsS "https://www.duckdns.org/update?domains=vitalify&token=<token>&ip=" >/dev/null
   ```
3. Comprueba que el dominio responde con la IP de la VM: `dig +short vitalify.duckdns.org`.

Caddy necesita el dominio apuntando a la VM y los puertos 80 y 443 abiertos para obtener el certificado.

### 4. Crear el `.env` en el servidor

El `.env` vive **solo en el servidor** y nunca en git. Parte de la plantilla:

```bash
cp .env.example .env
chmod 600 .env
nano .env
```

| Variable | Valor en producción |
|---|---|
| `DOMINIO` | El dominio del paso 3, sin `https://` (por ejemplo `vitalify.duckdns.org`). Lo usan Caddy y Mosquitto |
| `ConnectionStrings__Transaccional` y `ConnectionStrings__Historial` | La misma cadena del **Session pooler** de Supabase (puerto 5432, `SSL Mode=Require`). Cada contexto usa su propio esquema y su propio pool de 5: juntos, 10 conexiones como máximo (la API no arranca si suman más) |
| `Jwt__Clave` | Una clave nueva: `openssl rand -base64 48` |
| `Seed__AdminCorreo`, `Seed__AdminNombre` y `Seed__AdminContrasena` | Solo si la base todavía no tiene administrador. Después se puede borrar la contraseña |
| `Mqtt__Usuario` y `Mqtt__Clave` | `vitalify-backend` y una clave larga (`openssl rand -base64 24`). La misma clave va en el `passwd` de Mosquitto (paso 5) |
| `Cors__Origenes` | Los orígenes del dashboard separados por comas (por ejemplo `https://dashboard.vitalify.pe`). Nunca `*` |
| `Push__*` | Si se usa FCM, la ruta al JSON de Firebase, que vive fuera del repositorio |

`docker-compose.prod.yml` ya fija para la API `ASPNETCORE_ENVIRONMENT=Production`, el broker (`mosquitto:1883`, sin TLS por la red interna) y `Proxy__RedesConocidas` (la subred interna `172.30.57.0/24`, donde está Caddy). En Production no existen el simulador, `POST /dev/telemetria` ni Swagger (salvo con `Swagger__Habilitado=true`), y `Seed__DatosDemo` se ignora.

**Migraciones.** El servidor nunca migra. Se aplican desde tu máquina, después de revisar el SQL con `dotnet ef migrations script`:

```bash
dotnet ef database update --context TransaccionalDbContext --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef database update --context HistorialDbContext     --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
```

### 5. Crear las claves MQTT

Mosquitto no acepta clientes anónimos. Las claves se guardan con hash en `deploy/mosquitto/secretos/passwd`, que está en `.gitignore`. `mosquitto_passwd` pide cada clave dos veces, así que no queda en el historial de la terminal.

```bash
mkdir -p deploy/mosquitto/secretos

# El backend: la misma clave que Mqtt__Clave del .env. -c crea el archivo (solo la primera vez).
docker compose -f docker-compose.prod.yml run --rm --no-deps --entrypoint mosquitto_passwd mosquitto -c /mosquitto/secretos/passwd vitalify-backend

# Cada wearable: el usuario es su código en Vitalify y la clave es MQTT_CLAVE de su secretos.h.
docker compose -f docker-compose.prod.yml run --rm --no-deps --entrypoint mosquitto_passwd mosquitto /mosquitto/secretos/passwd ESP32-001
```

### 6. Levantar

```bash
docker compose -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f mosquitto   # espera "Certificado de ... copiado" y "running"
```

Lo que pasa al levantar:

1. Caddy obtiene el certificado del dominio, normalmente en unos segundos.
2. Mosquitto espera ese certificado (hasta 5 min), lo copia con permisos para su usuario y abre el 1883 interno y el 8883 con TLS.
3. La API se conecta al broker y reintenta hasta que esté listo.

### 7. Verificar

```bash
curl https://vitalify.duckdns.org/health
```

Debe responder `"estado": "Healthy"` con `transaccional`, `historial` y `mqtt`; en `mqtt`, la descripción `Conectado a mosquitto:1883.`. Además:

- `http://vitalify.duckdns.org` redirige a HTTPS.
- `GET /api/v1/sistema/almacenamiento`, con un token de administrador, muestra cuánto ocupa la base.
- Un wearable de prueba, o `mosquitto_pub` con TLS, publica una lectura:
  ```bash
  mosquitto_pub -h vitalify.duckdns.org -p 8883 --capath /etc/ssl/certs -u ESP32-001 -P '<clave>' -q 1 \
    -t device/ESP32-001/telemetria -m '{"dispositivo":"ESP32-001","ts":"<ahora en UTC>","seq":1,"fc":78,"spo2":97,"temp":36.5}'
  ```
  La lectura aparece en el historial del paciente vinculado a `ESP32-001`. Sin usuario, el broker rechaza la conexión.

### 8. Recargar el certificado de Mosquitto (cron semanal)

Caddy renueva el certificado solo, cada ~60 días, pero Mosquitto trabaja con una copia. [`scripts/recargar-cert-mosquitto.sh`](../scripts/recargar-cert-mosquitto.sh) copia el certificado vigente y le envía `SIGHUP` al broker (`docker compose kill -s HUP mosquitto`). Mosquitto lo recarga sin cortar las conexiones de los wearables. Prográmalo una vez por semana:

```bash
crontab -e
0 4 * * 1  /home/<usuario>/Vitalify-Backend/scripts/recargar-cert-mosquitto.sh >> /home/<usuario>/vitalify-cert.log 2>&1
```

Con una vez por semana alcanza: el certificado vale 90 días y Caddy lo renueva cuando le quedan unos 30.

### 9. Actualizar

```bash
cd Vitalify-Backend
git pull
# si el pull trae migraciones nuevas: aplícalas desde tu máquina (paso 4) ANTES de levantar
docker compose -f docker-compose.prod.yml up -d --build
```

## Operación

### Un wearable nuevo

1. El administrador registra el dispositivo en Vitalify (`POST /api/v1/dispositivos`), por ejemplo `ESP32-005`.
2. En el servidor, crea su clave MQTT y recarga el broker:
   ```bash
   scripts/agregar-wearable-mqtt.sh ESP32-005
   ```
   El script ejecuta `mosquitto_passwd` como el usuario `mosquitto` y después `docker compose kill -s HUP mosquitto`. Mosquitto vuelve a leer el `passwd` sin cortar las conexiones.
3. En el firmware, el código y esa clave van en `config.h` y `secretos.h` (ver ["Firmware en producción"](contrato-telemetria.md#firmware-en-producción)).

Para quitarle el acceso a un wearable: `docker compose -f docker-compose.prod.yml exec -u mosquitto mosquitto mosquitto_passwd -D /mosquitto/secretos/passwd ESP32-005` y la misma recarga (`kill -s HUP`).

### Logs

```bash
docker compose -f docker-compose.prod.yml logs -f api        # JSON; nunca claves, tokens ni datos personales
docker compose -f docker-compose.prod.yml logs -f mosquitto  # conexiones, nunca el contenido de los mensajes
docker compose -f docker-compose.prod.yml logs -f caddy      # certificados y accesos
```

### Espacio en Supabase (plan gratuito: 500 MB)

- `GET /api/v1/sistema/almacenamiento` (solo el administrador) da el tamaño de toda la base y de cada tabla.
- La **retención** corre todos los días a las 03:00 de Lima. Las lecturas crudas de los episodios egresados hace más de 30 días (`Historial__DiasRetencionCrudo`) pasan a `lectura_signos_resumen`, cada 5 min, y se borran. Ver [historial-clinico.md](historial-clinico.md#retención-del-historial).
- `evaluacion_riesgo` guarda una fila del wearable solo si cambia el riesgo o pasaron 5 min ([motor-clinico.md](motor-clinico.md)).

### Pausa del plan gratuito de Supabase

Supabase pausa un proyecto gratuito tras **7 días sin actividad**. Con la API desplegada y un wearable enviando datos no pasa: el vigilante de alertas consulta la base cada 30 s y cada lectura escribe en ella. Si el sistema queda apagado más de una semana, hay que reanudar el proyecto desde el panel de Supabase antes de levantarlo.

### Si algo falla

| Síntoma | Causa probable |
|---|---|
| `mosquitto` reinicia con "Caddy no tiene todavía el certificado" | El dominio no apunta a la VM o los puertos 80/443 están cerrados. Revisa `logs caddy` |
| `mosquitto` sale con "Falta /mosquitto/secretos/passwd" | Falta el paso 5 |
| `/health` con `mqtt: Unhealthy` | La clave de `vitalify-backend` en el `passwd` no es la de `Mqtt__Clave`, o el broker no arrancó |
| La API no arranca: "Maximum Pool Size suman ..." | Las dos cadenas apuntan a Supabase y sus pools suman más de 10 |
| La API no arranca: "Cors__Origenes no admite '*'" | Lista los orígenes del dashboard uno por uno |
| El wearable no conecta por 8883 | Hora sin sincronizar (NTP), raíz equivocada o usuario y clave que no están en el `passwd` |

## Probarlo en la laptop (sin dominio)

Con un nombre `*.localhost`, Caddy usa su propia CA local en vez de Let's Encrypt. Es lo que se usó para verificar esta configuración:

```bash
mkdir -p deploy/mosquitto/secretos   # crea el passwd como en el paso 5
DOMINIO=vitalify.localhost docker compose -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.prod.yml cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root.crt
curl --cacert caddy-root.crt https://vitalify.localhost/health

# El wearable desde fuera, por el 8883 publicado, validando el certificado con la CA de Caddy:
docker run --rm --add-host vitalify.localhost:host-gateway -v "$PWD/caddy-root.crt:/ca.crt:ro" eclipse-mosquitto:2.0.20 \
  mosquitto_pub -h vitalify.localhost -p 8883 --cafile /ca.crt -u ESP32-001 -P '<clave>' -q 1 \
  -t device/ESP32-001/telemetria -m '{"dispositivo":"ESP32-001","ts":"<ahora en UTC>","seq":1,"fc":78,"spo2":97,"temp":36.5}'

DOMINIO=vitalify.localhost docker compose -f docker-compose.prod.yml down -v
```

Con el `.env` de siempre, esa API usa la misma base de Supabase que el desarrollo.

## Por qué Mosquitto y no un broker en la nube

HiveMQ Cloud ya no tiene plan gratuito. El plan Serverless se retira el 31/12/2026 y no admite clústeres nuevos, y el Starter es una prueba de 15 días que después se cobra por hora. Por eso se usa el Mosquitto del proyecto IoT, con el mismo ACL, en el mismo servidor que la API. El backend funciona con cualquier broker con TLS: `Mqtt__UsarTls=true` y `Mqtt__ValidarCertificado` (siempre `true` fuera de Development).

## Por qué el historial se queda en Supabase

TimescaleDB ya no se usa. El historial vive en el mismo Supabase, en el esquema `vitalify_historial`, con su propio `HistorialDbContext` y su propia cadena de conexión. No tiene claves foráneas hacia el esquema transaccional ni usa nada propio de Supabase. Si algún día crece más de lo que admite el plan, podría pasar a otra base PostgreSQL, por ejemplo TimescaleDB, **cambiando solo `ConnectionStrings__Historial`** y aplicando ahí las migraciones del contexto.
