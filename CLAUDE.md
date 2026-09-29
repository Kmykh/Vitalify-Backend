# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Contexto

Backend de **Vitalify** (tesis UPC): plataforma IoMT hospitalaria. Un wearable ESP32 envía signos vitales, el backend calcula NEWS2 y MEWS y genera alertas para enfermería (app Flutter) y médicos (dashboard React). ASP.NET Core **.NET 8**, arquitectura hexagonal, PostgreSQL en **Supabase** usado solo como base de datos. No se usa Supabase Auth ni su SDK: el login será un JWT propio.

El código, los nombres del dominio y los mensajes de commit van en **español**.

## Commits

Se usa [Conventional Commits](https://www.conventionalcommits.org/es/) en español: `tipo(ámbito): descripción en presente y minúscula`. Los tipos son `feat`, `fix`, `test`, `docs`, `refactor`, `build`, `ci` y `chore`. Los ámbitos son la capa o el área afectada: `dominio`, `aplicacion`, `persistencia`, `api`, `arquitectura`, `docker`. Ejemplo: `feat(api): agrega el endpoint de login con JWT`. Se hace un commit por paso lógico, y el repositorio es público en GitHub (`Kmykh/Vitalify-Backend`).

## Fases

0 base · 1 usuarios, JWT y RBAC · 2 pacientes y sensores · 3 ingesta simulada · 4 NEWS2/MEWS · 5 alertas · 6 consultas · 7 IoT (Mosquitto, MQTT, ESP32), TimescaleDB y despliegue.

No adelantar trabajo de fases posteriores: TimescaleDB, MQTT, SignalR y push llegan en la fase 7. Las entidades de negocio empiezan en la fase 1.

## Comandos

```bash
dotnet build                     # TreatWarningsAsErrors: cualquier warning rompe el build
dotnet test                      # no necesita base de datos
dotnet test --filter "FullyQualifiedName~ReglasDeDependencia"          # una clase de pruebas
dotnet test --filter "FullyQualifiedName~Domain_no_depende_de_otras"   # una prueba
dotnet run --project src/Vitalify.Api        # http://localhost:5080/swagger
docker compose up --build                    # http://localhost:8080

dotnet tool restore              # instala dotnet-ef 8 (herramienta local; la global puede ser otra versión)
dotnet ef database update --context TransaccionalDbContext --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef migrations add <Nombre> --context TransaccionalDbContext -o Persistence/Transaccional/Migrations --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef migrations add <Nombre> --context HistorialDbContext -o Persistence/Historial/Migrations --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
```

Las migraciones se aplican solo con `dotnet ef database update`. Nunca se aplican al arrancar la API.

## Arquitectura hexagonal

- `Vitalify.Domain`: C# puro, **sin paquetes NuGet ni referencias a otros proyectos**.
- `Vitalify.Application`: casos de uso y puertos (interfaces). Depende **solo** de Domain. Sin EF Core, Npgsql ni ASP.NET.
- `Vitalify.Infrastructure`: adaptadores de salida (EF Core, repositorios, health checks). Depende de Application y Domain, **nunca de Api**. Expone `AddInfrastructure(IServiceCollection, IConfiguration)`.
- `Vitalify.Api`: adaptadores de entrada (controladores REST, Swagger) y raíz de composición (DI).

`tests/Vitalify.Architecture.Tests` verifica estas reglas con NetArchTest, con las referencias de cada ensamblado y leyendo el `.csproj` de Domain y Application. Si una regla falla, se corrige el diseño, no la prueba. Cada capa tiene una clase `AssemblyReference` que sirve de marcador para esas pruebas.

`Directory.Build.props` fija para todos los proyectos: net8.0, Nullable, ImplicitUsings, TreatWarningsAsErrors y `<Version>` (que `/api/v1/ping` expone). `global.json` fija el SDK 8.

## Persistencia: dos DbContext

Ambos viven en `src/Vitalify.Infrastructure/Persistence/`:

| Contexto | Esquema | Cadena | Contenido |
|---|---|---|---|
| `TransaccionalDbContext` | `vitalify` | `ConnectionStrings:Transaccional` | usuarios, pacientes, alertas (desde la fase 1) |
| `HistorialDbContext` | `vitalify_historial` | `ConnectionStrings:Historial` | signos vitales y evaluaciones de riesgo (desde la fase 3) |

- Nunca usar el esquema `public`: Supabase lo expone por su API de datos.
- Cada contexto guarda su `__EFMigrationsHistory` en su propio esquema (`OpcionesNpgsql.Configurar`) y sus migraciones en `Persistence/<Contexto>/Migrations`.
- Las configuraciones de entidades (`IEntityTypeConfiguration`) se aplican por namespace: pon las de cada contexto bajo `Persistence.Transaccional` o `Persistence.Historial`.
- **`HistorialDbContext` pasará a TimescaleDB en la fase 7.** Solo cambiará su cadena de conexión y se agregará una migración. Por eso no debe usar nada propio de Supabase y los casos de uso deben acceder al historial por su propio puerto.
- `CadenaDeConexion.Obtener` lee la cadena y acepta tanto el formato clave=valor de Npgsql como una URI `postgresql://`, que convierte agregando `SSL Mode=Require`. En ambos formatos usa `Maximum Pool Size=5` si la cadena no lo fija. El pool es pequeño porque el pooler gratuito de Supabase admite pocas conexiones.
- `FabricasDeDiseno.cs` tiene los `IDesignTimeDbContextFactory` que usa `dotnet ef`. Cargan el `.env` igual que la API.

## Configuración y secretos

- **Nunca se escriben contraseñas ni cadenas de conexión en archivos versionados.** Van solo en `.env`, en la raíz, que está en `.gitignore` y `.dockerignore`. No usar `dotnet user-secrets` ni ponerlas en `appsettings*.json`. `.env.example` es la plantilla sin datos reales.
- `Program.cs` llama a `DotNetEnv.Env.TraversePath().Load()` antes de crear el builder. Las variables `ConnectionStrings__X` se leen como `ConnectionStrings:X`. En Docker, `docker-compose.yml` pasa el mismo archivo con `env_file`.

## Convenciones de la API

- Rutas versionadas: `api/v1/...`. Los controladores van en `Controllers/V1`.
- Errores: `ManejadorGlobalDeExcepciones` (`IExceptionHandler`) y `AddProblemDetails` con `traceId`. Todo error sale como ProblemDetails y el detalle y el stack trace solo se muestran en Development.
- `/health` usa los checks `transaccional` e `historial` (AspNetCore.HealthChecks.NpgSql, registrados en `AddInfrastructure`) y responde un JSON propio (`RespuestaHealth`).
- Swagger solo en Development, con comentarios XML y el esquema Bearer JWT ya definido (botón Authorize) para la fase 1.
- Logs con `ILogger` en JSON por consola. Los orígenes de CORS se configuran en `Cors:OrigenesPermitidos` (appsettings).
- La configuración de la API está en `src/Vitalify.Api/Configuracion/`.
