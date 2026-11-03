# Vitalify — Backend

Vitalify es una plataforma IoMT hospitalaria (tesis UPC). Monitorea los signos vitales de pacientes hospitalizados con un wearable ESP32, calcula los puntajes de riesgo NEWS2 y MEWS en el backend y genera alertas para enfermería (app Flutter) y para los médicos (dashboard React). Las alertas llegan en tiempo real (SignalR) y por push (FCM) a quien está de turno.

Este repositorio contiene el backend: **ASP.NET Core 8** con **arquitectura hexagonal** y **PostgreSQL en Supabase** (usado solo como base de datos, sin Supabase Auth ni su SDK).

## Estructura

```
src/
  Vitalify.Domain/          Entidades y reglas del dominio (C# puro, sin paquetes NuGet)
  Vitalify.Application/     Casos de uso y puertos (depende solo de Domain)
  Vitalify.Infrastructure/  Adaptadores de salida: EF Core, repositorios
  Vitalify.Api/             Adaptadores de entrada: REST, Swagger; raíz de composición
tests/
  Vitalify.Domain.Tests/            Pruebas unitarias del dominio
  Vitalify.Application.Tests/       Casos de uso con fakes en memoria
  Vitalify.Architecture.Tests/      Verifican las reglas de dependencia entre capas
  Vitalify.Api.IntegrationTests/    API completa contra PostgreSQL en contenedor (Testcontainers)
docs/
  matriz-permisos.md                Roles contra módulos (HU03)
```

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/), para las pruebas de integración y para correr la API en un contenedor
- `dotnet-ef` 8: viene como herramienta local del repositorio. Instálala con:
  ```bash
  dotnet tool restore
  ```
- Un proyecto de [Supabase](https://supabase.com) (el plan gratuito basta)

## Configuración: archivo `.env`

Las cadenas de conexión, la clave JWT y los datos del primer administrador se leen **solo** del archivo `.env` en la raíz del repositorio. Ese archivo está en `.gitignore` y nunca se sube. Si una variable también está definida en el entorno (Docker, CI), gana la del entorno.

1. Copia la plantilla:
   ```bash
   cp .env.example .env
   ```
2. En Supabase, abre tu proyecto, pulsa **Connect** y elige el método **Session pooler** (puerto 5432, IPv4).
3. Reemplaza los valores entre `< >` en las dos variables. Por ahora las dos usan la misma cadena:
   ```
   ConnectionStrings__Transaccional=Host=aws-0-<región>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref-del-proyecto>;Password=<contraseña>;SSL Mode=Require;Maximum Pool Size=5
   ConnectionStrings__Historial=<la misma cadena>
   ```
   - La contraseña es la **de la base de datos del proyecto**, no la de tu cuenta de Supabase.
   - También puedes pegar la cadena en formato URI tal como la muestra Supabase (`postgresql://postgres.<ref>:<contraseña>@<host>:5432/postgres`). La API la convierte sola y le agrega `SSL Mode=Require` y `Maximum Pool Size=5`. Si la contraseña tiene caracteres especiales dentro de la URI, deben ir codificados (`@` → `%40`, `:` → `%3A`, etc.).
   - Si el valor contiene `#` o espacios, ponlo entre comillas dobles.
   - El pool es pequeño (5) porque el pooler gratuito de Supabase admite pocas conexiones y hay dos contextos. Si la cadena no incluye `Maximum Pool Size`, la API usa 5 igualmente.
4. Genera la clave para firmar los JWT y pégala en `Jwt__Clave`:
   ```bash
   openssl rand -base64 48
   ```
   Debe tener al menos 32 caracteres; si falta o es más corta, la API no arranca y lo indica. Cambiarla invalida todos los tokens emitidos. Las demás variables `Jwt__*` ya traen sus valores en la plantilla (access token de 15 min, 30 min de inactividad y sesión máxima de 12 h).
5. Configura el **primer administrador** con `Seed__AdminCorreo`, `Seed__AdminNombre` y `Seed__AdminContrasena` (al menos 8 caracteres, con una letra y un número).
   - Al arrancar, si no existe ningún usuario con rol `Administrador`, la API lo crea con esos datos. Si ya existe uno, no hace nada.
   - Si faltan las variables, no crea nada y escribe una advertencia en el log. No hay valores por defecto.
   - Una vez creado, puedes borrar `Seed__AdminContrasena` del `.env`.
   - Los administradores no se crean por la API: el administrador registra médicos y enfermeras con `POST /api/v1/usuarios`.
6. Opcional, solo en Development: `Seed__DatosDemo=true` crea al arrancar el servicio `MEDICINA-B` ("Medicina B"), las camas `MED-B-01` a `MED-B-06` y los sensores `ESP32-001` a `ESP32-004` que falten. No crea pacientes ni personal.
7. Opcional: push real por Firebase con `Push__Habilitado=true` y `Push__RutaCredencialesFirebase` (el JSON de la cuenta de servicio va **fuera del repositorio**). Apagado, el push solo se escribe en el log. Ver [`docs/alertas-y-turnos.md`](docs/alertas-y-turnos.md#push-fcm).

## Aplicar las migraciones

Las migraciones **no** se aplican al arrancar la API. Se aplican a mano, una vez por contexto:

```bash
dotnet ef database update --context TransaccionalDbContext --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef database update --context HistorialDbContext     --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
```

El esquema `vitalify_historial` guarda la telemetría (fase 3): `lectura_signos`, `estado_signos_actual` e `incidencia_telemetria`; y el motor clínico (fases 4 y 5): `observacion_enfermeria` y `evaluacion_riesgo`.

Después, en el **Table Editor** de Supabase aparecen los esquemas `vitalify` y `vitalify_historial`, cada uno con su tabla `__EFMigrationsHistory`. El esquema `vitalify` tiene además `usuario`, `sesion_refresco`, `token_revocado` y `auditoria` (fase 1) y `cama`, `dispositivo`, `paciente`, `hospitalizacion` y `asignacion_dispositivo` (fase 2); `codigo_vinculacion` (fase 5); `servicio`, `programacion_turno`, `alerta_clinica` y `dispositivo_movil` (fase 6); y `nota_clinica` y `nota_clinica_version` (fase 7).

## Levantar el proyecto

### Con .NET

```bash
dotnet run --project src/Vitalify.Api
```

- Swagger: <http://localhost:5080/swagger>
- Ping: <http://localhost:5080/api/v1/ping>
- Salud: <http://localhost:5080/health>

### Con Docker

```bash
docker compose up --build
```

Usa el mismo `.env` (con `env_file`). La API queda en <http://localhost:8080> (`/swagger`, `/api/v1/ping`, `/health`).

## Endpoints

| Método | Ruta | Quién | Descripción |
|---|---|---|---|
| POST | `/api/v1/auth/login` | anónimo | Devuelve `accessToken`, `expiraEn`, `refreshToken` y `usuario` (con su `rol`). Máximo 5 intentos por minuto por IP |
| POST | `/api/v1/auth/refresh` | anónimo | Entrega un nuevo par de tokens y rota el refresh token |
| POST | `/api/v1/auth/logout` | autenticado | Invalida el access token actual y el refresh token enviado |
| GET | `/api/v1/auth/yo` | autenticado | Datos del usuario actual |
| POST | `/api/v1/usuarios` | Administrador | Registra un médico o una enfermera, con su `servicioId` (obligatorio) |
| PUT | `/api/v1/usuarios/{id}/servicio` | Administrador | Cambia el servicio de una enfermera o un médico |
| GET | `/api/v1/usuarios?pagina=1&tamano=20` | Administrador | Lista paginada |
| GET | `/api/v1/usuarios/{id}` | Administrador | Detalle de un usuario |
| POST | `/api/v1/servicios` | Administrador | Registra un servicio (`codigo` único, por ejemplo `MEDICINA-B`, y `nombre`) |
| GET | `/api/v1/servicios` | todos | Servicios del hospital |
| PUT | `/api/v1/servicios/{id}/programacion/{anio}/{mes}` | Administrador | Carga la planilla del mes (letras y turnos por día); 400 por día y letra si hay cruces |
| GET | `/api/v1/servicios/{id}/programacion/{anio}/{mes}` | todos | La planilla cargada, con letras y nombres |
| GET | `/api/v1/servicios/{id}/entrega-turno` | Médico o Enfermera | Reporte de enfermería: pacientes por riesgo, tendencia en el turno, alertas, próxima toma y sensor |
| GET | `/api/v1/turnos/actual?servicioId=` | Médico o Enfermera | Enfermeras de turno ahora (o el respaldo si no hay programación) |
| GET | `/api/v1/turnos/mios?desde=&hasta=` | Médico o Enfermera | Turnos propios (máximo 62 días) |
| POST | `/api/v1/camas` | Administrador | Registra una cama (código único, por ejemplo `MED-B-05`) en un servicio (`servicioId`) |
| GET | `/api/v1/camas?soloDisponibles=true` | todos | Camas con su ocupación, sin datos del paciente |
| POST | `/api/v1/dispositivos` | Administrador | Registra un wearable (código `^[A-Z0-9-]{3,32}$`, por ejemplo `ESP32-001`) |
| PATCH | `/api/v1/dispositivos/{id}/estado` | Administrador | `Mantenimiento`, `DadoDeBaja` o `Disponible` (reactivar) |
| GET | `/api/v1/dispositivos?estado=Disponible` | todos | Dispositivos; si están asignados, la cama |
| POST | `/api/v1/dispositivos/{id}/qr` | todos | Genera y publica un QR de vinculación nuevo en la pantalla del sensor (no devuelve el código) |
| POST | `/api/v1/vinculaciones/escaneo` | Enfermera | Valida el QR del wearable (`VTF1:{codigo}:{codigo QR}`) y devuelve un token de vinculación. 10 por minuto por usuario |
| POST | `/api/v1/vinculaciones/confirmar` | Enfermera | Ingreso (o reingreso), sensor, observación inicial y primer NEWS2/MEWS en un paso; el wearable recibe "vinculado" |
| POST | `/api/v1/pacientes` | Enfermera | Ingreso: datos básicos (`fechaNacimiento` o `edad`), cama y diagnóstico (HU05) |
| PUT | `/api/v1/pacientes/{id}` | Enfermera | Corrige los datos básicos, el diagnóstico y la `confusionBasal` |
| GET | `/api/v1/pacientes?pagina=&tamano=&buscar=` | Médico o Enfermera | Pacientes hospitalizados |
| GET | `/api/v1/pacientes/{id}` | Médico o Enfermera | Ficha con la hospitalización, la cama y el sensor (auditada) |
| POST | `/api/v1/pacientes/{id}/dispositivo` | Enfermera | Vincula un sensor disponible sin QR (respaldo de la HU06); publica "vinculado" |
| DELETE | `/api/v1/pacientes/{id}/dispositivo` | Enfermera | Libera el sensor; el wearable recibe "desvinculado" y un QR nuevo |
| POST | `/api/v1/pacientes/{id}/egreso` | Enfermera | Egreso con motivo; libera el sensor en la misma transacción (HU08) |
| GET | `/api/v1/pacientes/monitoreados` | Médico o Enfermera | Pacientes con sensor (HU07), de mayor a menor riesgo (HU18), con NEWS2, MEWS, nivel de riesgo, `senal`, `conexionSensor` y `alertasAbiertas` |
| GET | `/api/v1/pacientes/{id}/signos/actual` | Médico o Enfermera | Último valor de cada signo con `vigente`, `pendiente-actualizacion` o `sin-datos` |
| GET | `/api/v1/pacientes/{id}/hospitalizaciones` | Médico o Enfermera | Episodios del paciente: ingreso, egreso y motivo, cama, servicio, diagnóstico y sensores usados |
| GET | `/api/v1/pacientes/{id}/signos/historial?hospitalizacionId=&desde=&hasta=&agrupar=` | Médico o Enfermera | Lecturas del episodio (por defecto el activo o el último, completo); sin agrupar hasta 7 días; `agrupar=1m\|5m\|15m\|1h` da promedio, mínimo y máximo por intervalo |
| GET | `/api/v1/pacientes/{id}/riesgo/historial?hospitalizacionId=&desde=&hasta=&agrupar=` | Médico o Enfermera | Evaluaciones NEWS2/MEWS con puntos y faltantes; `agrupar=5m\|15m\|1h` da mínimo, máximo, último y peor nivel |
| GET | `/api/v1/pacientes/{id}/observaciones?hospitalizacionId=&desde=&hasta=` | Médico o Enfermera | Tomas manuales completas con el nombre de quien las registró |
| GET | `/api/v1/pacientes/{id}/resumen?hospitalizacionId=` | Médico o Enfermera | Vista del médico: paciente, episodio, último riesgo, extremos, alertas por tipo con tiempo medio de respuesta, últimas 5 notas y próxima toma |
| GET | `/api/v1/pacientes/{id}/notas?hospitalizacionId=` | Médico o Enfermera | Notas clínicas del episodio con su autor (HU22) |
| POST | `/api/v1/pacientes/{id}/notas` | Médico o Enfermera | Registra una nota (`Evolucion`, `Enfermeria` o `Interconsulta`) en la hospitalización activa |
| PUT | `/api/v1/notas/{id}` | Autor | Corrige la nota dentro de las 24 h; la versión anterior se conserva |
| GET | `/api/v1/notas/{id}/versiones` | Médico o Enfermera | Versiones anteriores de la nota |
| GET | `/api/v1/auditoria?accion=&usuarioId=&desde=&hasta=&pagina=` | Administrador | Auditoría de seguridad: solo acciones e ids |
| GET | `/api/v1/telemetria/incidencias?desde=&hasta=&dispositivo=&tipo=` | Administrador | Log de incidencias técnicas de la telemetría |
| GET | `/api/v1/pacientes/{id}/riesgo` | Médico o Enfermera | NEWS2 y MEWS (total, nivel, completo, puntos y faltantes), `recomendacion`, `proximaTomaManualEn`, `avisos`, `escalaSpo2` y `ultimoBrazoPa` |
| POST | `/api/v1/pacientes/{id}/observaciones` | Médico o Enfermera | Toma manual con el asistente (presión con 1 a 3 lecturas, conciencia ACVPU por respuestas, oxígeno, FR, contexto y preocupación clínica) y recalcula el riesgo |
| PUT | `/api/v1/pacientes/{id}/escala-spo2` | Médico | Indica la escala de SpO2 (1 o 2) del paciente y recalcula el riesgo |
| GET | `/api/v1/guias/toma-manual` | Médico o Enfermera | Guía versionada del asistente: preguntas, recomendaciones y fuentes |
| GET | `/api/v1/alertas?estado=&servicioId=&tipo=&desde=&hasta=&pagina=` | Médico o Enfermera | Alertas de su servicio; por defecto las abiertas, por prioridad y antigüedad |
| POST | `/api/v1/alertas/{id}/tomar` | Médico o Enfermera | La alerta pasa a `en-atencion` |
| POST | `/api/v1/alertas/{id}/atender` | Médico o Enfermera | Cierra la alerta con una nota obligatoria (5 a 1000 caracteres) |
| GET | `/api/v1/pacientes/{id}/alertas?hospitalizacionId=` | Médico o Enfermera | Alertas del episodio con los tiempos de respuesta (historial: cualquier servicio, auditado) |
| POST | `/api/v1/notificaciones/dispositivos` | Médico o Enfermera | Registra el token de FCM del celular (`token`, `plataforma`) |
| DELETE | `/api/v1/notificaciones/dispositivos` | Médico o Enfermera | Quita el token (al cerrar sesión), con el mismo cuerpo |
| WS | `/hubs/monitoreo` | Médico o Enfermera | Hub de SignalR: `alertaCreada`, `alertaActualizada`, `riesgoActualizado` y `signosActualizados` |
| POST | `/api/v1/dev/telemetria` | Administrador | **Solo Development:** envía una lectura como el ESP32 |
| GET | `/api/v1/ping` | anónimo | `{ "servicio": "vitalify-api", "version": "0.1.0", "hora": <UTC> }` |
| GET | `/health` | anónimo | Estado de las conexiones `transaccional` e `historial` (200 o 503) |
| GET | `/swagger` | anónimo | Documentación OpenAPI (solo en Development). Botón **Authorize**: pega el `accessToken` |

Los errores salen como ProblemDetails en español. Su `type` identifica el caso: `credenciales-invalidas`, `sesion-expirada`, `correo-en-uso`, `acceso-denegado`, etc. Los permisos de cada rol están en [`docs/matriz-permisos.md`](docs/matriz-permisos.md), las alertas, los turnos y las notificaciones, en [`docs/alertas-y-turnos.md`](docs/alertas-y-turnos.md), y el historial y las notas, en [`docs/historial-clinico.md`](docs/historial-clinico.md).

### Telemetría con el wearable real (MQTT)

El firmware y el broker están en el proyecto IoT (`../IOT/tesis_V01`). Para recibir las lecturas reales:

1. Levanta el broker: `cd ../IOT/tesis_V01/broker && docker compose up -d`.
2. En el `.env` del backend:

   ```
   Mqtt__Habilitado=true
   Mqtt__Servidor=localhost
   Mqtt__Puerto=1883
   Mqtt__Usuario=vitalify-backend
   Mqtt__Clave=<MQTT_BACKEND_CLAVE de broker/.env>
   ```

3. Arranca la API. `/health` debe mostrar `mqtt: Healthy`.
4. Registra el sensor `ESP32-001` (o `Seed__DatosDemo=true`) y enciende el wearable en la misma red Wi-Fi que la laptop.
5. **Vincúlalo por QR:** al conectarse, el wearable muestra un QR. La enfermera lo escanea (`POST /vinculaciones/escaneo`) y confirma paciente, cama y toma inicial (`POST /vinculaciones/confirmar`). El wearable pasa a "vinculado" y empieza a enviar. El flujo completo está en [`docs/vinculacion-qr.md`](docs/vinculacion-qr.md).

Las lecturas aparecen en `/pacientes/{id}/signos/actual` y el riesgo en `/pacientes/{id}/riesgo`. Cómo se calcula NEWS2/MEWS con este hardware y la toma manual: [`docs/motor-clinico.md`](docs/motor-clinico.md). Los comandos que el backend envía al wearable: [`docs/contrato-telemetria.md`](docs/contrato-telemetria.md#comandos-al-wearable).

### Telemetría (sin hardware)

- El contrato del mensaje del ESP32 está en [`docs/contrato-telemetria.md`](docs/contrato-telemetria.md).
- Para generar lecturas sin el wearable, activa el simulador en el `.env`:

  ```
  Simulador__Habilitado=true
  Simulador__IntervaloSegundos=10
  Simulador__Escenarios__ESP32-001=Estable
  Simulador__Escenarios__ESP32-002=SensorDefectuoso
  ```

  Envía una lectura por cada sensor vinculado a un paciente. Los escenarios son `Estable`, `Deterioro`, `Caida`, `SensorDefectuoso` y `DeterioroRapido`.
- Déjalo en `false` cuando no lo uses: cada lectura ocupa espacio en Supabase.

### Alertas en tiempo real (sin hardware)

1. En el `.env`: `Seed__DatosDemo=true`, `Simulador__Habilitado=true` y `Simulador__Escenarios__ESP32-001=DeterioroRapido` (llega a NEWS2 ≥ 7 en 5 min).
2. Con el administrador:
   - busca el id de `MEDICINA-B` en `GET /api/v1/servicios`;
   - registra enfermeras y un médico con ese `servicioId`;
   - carga la planilla del mes con `PUT /api/v1/servicios/{id}/programacion/{anio}/{mes}`.
3. Con la enfermera de turno (`GET /api/v1/turnos/actual?servicioId=`), ingresa un paciente en `MED-B-01` y vincula `ESP32-001`.
4. Abre el cliente de ejemplo:

   ```bash
   python3 -m http.server 5500 -d docs/ejemplos
   # http://localhost:5500/cliente-signalr.html?api=http://localhost:5080&token=<accessToken de la enfermera>
   ```

5. Verás `alertaCreada` (media) y luego `alertaActualizada` (alta), el push en el log ("Push (solo log)") y, a los 5 min sin tomarla, el escalamiento (`escaladaEn` en `GET /api/v1/alertas`).

El detalle (disparadores, escalamiento, cambio de turno, eventos y push) está en [`docs/alertas-y-turnos.md`](docs/alertas-y-turnos.md).

### Sesión

- El **access token** (JWT) dura 15 minutos. Cuando vence, el cliente llama a `/auth/refresh` con su refresh token y recibe un par nuevo. El refresh token anterior deja de servir.
- La sesión **expira** si el refresh token no se usa en 30 minutos (inactividad) o cuando pasan 12 horas desde el login (un turno). En ambos casos `/auth/refresh` responde 401 con `type` `sesion-expirada`, y el cliente debe volver al login.
  - El cliente debe renovar el token solo cuando el usuario interactúa. Si lo renueva con un temporizador, la inactividad nunca se cumple.
- Si se presenta un refresh token que ya se rotó, se entiende como un posible robo y se revocan **todas** las sesiones del usuario. Por eso el cliente no debe hacer dos refresh en paralelo con el mismo token.
- En la base, el refresh token se guarda solo como hash SHA-256 y las contraseñas con PBKDF2.

## Pruebas

```bash
dotnet test
```

- Las pruebas unitarias y de arquitectura no necesitan nada más.
- Las de integración (`Vitalify.Api.IntegrationTests`) levantan un PostgreSQL 17 en Docker con Testcontainers, así que Docker Desktop debe estar encendido. **Nunca tocan Supabase.**
- Hay una prueba por cada escenario de las historias. Por ejemplo, para correr solo los de la HU04:

```bash
dotnet test --filter "FullyQualifiedName~HU04"
```

El CI (`.github/workflows/ci.yml`) ejecuta restore, build y todas las pruebas, incluidas las de integración, en cada push y pull request.

## Fases del backend

| Fase | Contenido                                     |
|------|-----------------------------------------------|
| 0    | Base del proyecto                             |
| 1    | Usuarios, JWT y RBAC                          |
| 2    | Pacientes, camas y sensores                   |
| 3    | Ingesta simulada de signos vitales            |
| 4    | Cálculo de NEWS2 y MEWS con el wearable real por MQTT |
| 5    | Vinculación por QR, comandos al wearable y asistente de toma manual |
| 6    | Servicios y turnos, alertas clínicas con escalamiento, SignalR, push (FCM) y entrega de turno |
| 7    | A: historial del paciente para el médico, notas clínicas y auditoría · B: producción con Supabase y Mosquitto con TLS (este estado) |
