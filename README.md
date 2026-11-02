# Vitalify — Backend

Vitalify es una plataforma IoMT hospitalaria (tesis UPC). Monitorea los signos vitales de pacientes hospitalizados con un wearable ESP32, calcula los puntajes de riesgo NEWS2 y MEWS en el backend y genera alertas para enfermería (app Flutter) y para los médicos (dashboard React).

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
6. Opcional, solo en Development: `Seed__DatosDemo=true` crea al arrancar las camas `MED-B-01` a `MED-B-06` (servicio "Medicina B") y los sensores `ESP32-001` a `ESP32-004` que falten. No crea pacientes.

## Aplicar las migraciones

Las migraciones **no** se aplican al arrancar la API. Se aplican a mano, una vez por contexto:

```bash
dotnet ef database update --context TransaccionalDbContext --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef database update --context HistorialDbContext     --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
```

El esquema `vitalify_historial` guarda la telemetría (fase 3): `lectura_signos`, `estado_signos_actual` e `incidencia_telemetria`.

Después, en el **Table Editor** de Supabase aparecen los esquemas `vitalify` y `vitalify_historial`, cada uno con su tabla `__EFMigrationsHistory`. El esquema `vitalify` tiene además `usuario`, `sesion_refresco`, `token_revocado` y `auditoria` (fase 1) y `cama`, `dispositivo`, `paciente`, `hospitalizacion` y `asignacion_dispositivo` (fase 2).

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
| POST | `/api/v1/usuarios` | Administrador | Registra un médico o una enfermera |
| GET | `/api/v1/usuarios?pagina=1&tamano=20` | Administrador | Lista paginada |
| GET | `/api/v1/usuarios/{id}` | Administrador | Detalle de un usuario |
| POST | `/api/v1/camas` | Administrador | Registra una cama (código único, por ejemplo `MED-B-05`) |
| GET | `/api/v1/camas?soloDisponibles=true` | todos | Camas con su ocupación, sin datos del paciente |
| POST | `/api/v1/dispositivos` | Administrador | Registra un wearable (código `^[A-Z0-9-]{3,32}$`, por ejemplo `ESP32-001`) |
| PATCH | `/api/v1/dispositivos/{id}/estado` | Administrador | `Mantenimiento`, `DadoDeBaja` o `Disponible` (reactivar) |
| GET | `/api/v1/dispositivos?estado=Disponible` | todos | Dispositivos; si están asignados, la cama |
| POST | `/api/v1/pacientes` | Enfermera | Ingreso: datos básicos (`fechaNacimiento` o `edad`), cama y diagnóstico (HU05) |
| PUT | `/api/v1/pacientes/{id}` | Enfermera | Corrige los datos básicos y el diagnóstico |
| GET | `/api/v1/pacientes?pagina=&tamano=&buscar=` | Médico o Enfermera | Pacientes hospitalizados |
| GET | `/api/v1/pacientes/{id}` | Médico o Enfermera | Ficha con la hospitalización, la cama y el sensor (auditada) |
| POST | `/api/v1/pacientes/{id}/dispositivo` | Enfermera | Vincula un sensor disponible (HU06) |
| DELETE | `/api/v1/pacientes/{id}/dispositivo` | Enfermera | Libera el sensor |
| POST | `/api/v1/pacientes/{id}/egreso` | Enfermera | Egreso con motivo; libera el sensor en la misma transacción (HU08) |
| GET | `/api/v1/pacientes/monitoreados` | Médico o Enfermera | Pacientes con sensor (HU07), con `ultimaLecturaEn` y `senal`. Puntajes NEWS2/MEWS en la fase 4 |
| GET | `/api/v1/pacientes/{id}/signos/actual` | Médico o Enfermera | Último valor de cada signo con `vigente`, `pendiente-actualizacion` o `sin-datos` |
| GET | `/api/v1/telemetria/incidencias?desde=&hasta=&dispositivo=&tipo=` | Administrador | Log de incidencias técnicas de la telemetría |
| POST | `/api/v1/dev/telemetria` | Administrador | **Solo Development:** envía una lectura como el ESP32 |
| GET | `/api/v1/ping` | anónimo | `{ "servicio": "vitalify-api", "version": "0.1.0", "hora": <UTC> }` |
| GET | `/health` | anónimo | Estado de las conexiones `transaccional` e `historial` (200 o 503) |
| GET | `/swagger` | anónimo | Documentación OpenAPI (solo en Development). Botón **Authorize**: pega el `accessToken` |

Los errores salen como ProblemDetails en español. Su `type` identifica el caso: `credenciales-invalidas`, `sesion-expirada`, `correo-en-uso`, `acceso-denegado`, etc. Los permisos de cada rol están en [`docs/matriz-permisos.md`](docs/matriz-permisos.md).

### Telemetría (sin hardware)

- El contrato del mensaje del ESP32 está en [`docs/contrato-telemetria.md`](docs/contrato-telemetria.md).
- Para generar lecturas sin el wearable, activa el simulador en el `.env`:

  ```
  Simulador__Habilitado=true
  Simulador__IntervaloSegundos=10
  Simulador__Escenarios__ESP32-001=Estable
  Simulador__Escenarios__ESP32-002=SensorDefectuoso
  ```

  Envía una lectura por cada sensor vinculado a un paciente. Los escenarios son `Estable`, `Deterioro`, `Caida` y `SensorDefectuoso`.
- Déjalo en `false` cuando no lo uses: cada lectura ocupa espacio en Supabase.

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
| 3    | Ingesta simulada de signos vitales (este estado) |
| 4    | Cálculo de NEWS2 y MEWS                       |
| 5    | Alertas                                       |
| 6    | Consultas                                     |
| 7    | IoT (Mosquitto, MQTT, ESP32), TimescaleDB y despliegue |
