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
  Vitalify.Domain.Tests/
  Vitalify.Application.Tests/
  Vitalify.Architecture.Tests/   Verifican las reglas de dependencia entre capas
```

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (solo para correr la API en un contenedor)
- `dotnet-ef` 8: viene como herramienta local del repositorio. Instálala con:
  ```bash
  dotnet tool restore
  ```
- Un proyecto de [Supabase](https://supabase.com) (el plan gratuito basta)

## Configuración: archivo `.env`

Las cadenas de conexión se leen **solo** del archivo `.env` en la raíz del repositorio. Ese archivo está en `.gitignore` y nunca se sube.

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

## Aplicar las migraciones

Las migraciones **no** se aplican al arrancar la API. Se aplican a mano, una vez por contexto:

```bash
dotnet ef database update --context TransaccionalDbContext --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef database update --context HistorialDbContext     --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
```

Después, en el **Table Editor** de Supabase aparecen los esquemas `vitalify` y `vitalify_historial`, cada uno con su tabla `__EFMigrationsHistory`.

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

## Endpoints de la fase 0

| Método | Ruta           | Descripción                                                                 |
|--------|----------------|-----------------------------------------------------------------------------|
| GET    | `/api/v1/ping` | `{ "servicio": "vitalify-api", "version": "0.1.0", "hora": <UTC> }`         |
| GET    | `/health`      | Estado de las conexiones `transaccional` e `historial` (200 o 503)          |
| GET    | `/swagger`     | Documentación OpenAPI (solo en Development)                                 |

## Pruebas

```bash
dotnet test
```

No necesitan base de datos. El CI (`.github/workflows/ci.yml`) ejecuta restore, build y test en cada push y pull request.

## Fases del backend

| Fase | Contenido                                     |
|------|-----------------------------------------------|
| 0    | Base del proyecto (este estado)               |
| 1    | Usuarios, JWT y RBAC                          |
| 2    | Pacientes y sensores                          |
| 3    | Ingesta simulada de signos vitales            |
| 4    | Cálculo de NEWS2 y MEWS                       |
| 5    | Alertas                                       |
| 6    | Consultas                                     |
| 7    | IoT (Mosquitto, MQTT, ESP32), TimescaleDB y despliegue |
