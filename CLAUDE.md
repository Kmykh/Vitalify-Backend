# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Contexto

Backend de **Vitalify** (tesis UPC): plataforma IoMT hospitalaria. Un wearable ESP32 envía signos vitales, el backend calcula NEWS2 y MEWS y genera alertas para enfermería (app Flutter) y médicos (dashboard React). ASP.NET Core **.NET 8**, arquitectura hexagonal, PostgreSQL en **Supabase** usado solo como base de datos. No se usa Supabase Auth ni su SDK: el login será un JWT propio.

El código, los nombres del dominio y los mensajes de commit van en **español**.

## Commits

Se usa [Conventional Commits](https://www.conventionalcommits.org/es/) en español: `tipo(ámbito): descripción en presente y minúscula`. Los tipos son `feat`, `fix`, `test`, `docs`, `refactor`, `build`, `ci` y `chore`. Los ámbitos son la capa o el área afectada: `dominio`, `aplicacion`, `persistencia`, `api`, `arquitectura`, `docker`. Ejemplo: `feat(api): agrega el endpoint de login con JWT`. Se hace un commit por paso lógico, y el repositorio es público en GitHub (`Kmykh/Vitalify-Backend`).

## Fases

0 base · 1 usuarios, JWT y RBAC · 2 pacientes y sensores · 3 ingesta simulada · 4 NEWS2/MEWS · 5 alertas · 6 consultas · 7 IoT (Mosquitto, MQTT, ESP32), TimescaleDB y despliegue.

Hechas:
- Fase 1 (HU01 a HU04): registro, login JWT, RBAC y cierre de sesión.
- Fase 2 (HU05, HU06, HU08 y la base de la HU07): pacientes, camas y sensores.
- Fase 3 (HU10, HU11 y HU12): ingesta de signos vitales con datos simulados.
- Fase 4: motor clínico NEWS2/MEWS, adaptado al hardware real. A pedido del usuario se adelantó de la fase 7 el **receptor MQTT**, para que el backend reciba lo que publica el wearable real (proyecto `../IOT/tesis_V01`, repositorio `Kmykh/IoT-Vitalfy`).
- Adelantado de la fase 6 a pedido del usuario: `GET /pacientes/{id}/signos/historial?desde=&hasta=` (`ObtenerHistorialSignos`, `IRepositorioLecturas.ListarPorHospitalizacionAsync`). Lee `lectura_signos` de la hospitalización activa, por defecto la última hora y como máximo 24 h, para los gráficos del dashboard React de prueba en `../Web`.

No adelantar trabajo de fases posteriores: alertas (5), consultas del dashboard (6); TimescaleDB, SignalR, push y despliegue (7). **No usar datos simulados para verificar lo que ya puede verificarse con el wearable real.**

## Comandos

```bash
dotnet build                     # TreatWarningsAsErrors: cualquier warning rompe el build
dotnet test                      # las de integración necesitan Docker encendido (Testcontainers); ninguna usa Supabase
dotnet test --filter "FullyQualifiedName~ReglasDeDependencia"          # una clase de pruebas
dotnet test --filter "FullyQualifiedName~HU04"                         # los escenarios de una historia
dotnet test tests/Vitalify.Domain.Tests                                # un proyecto
dotnet run --project src/Vitalify.Api        # http://localhost:5080/swagger
docker compose up --build                    # http://localhost:8080

cd ../IOT/tesis_V01/broker && docker compose up -d                 # broker Mosquitto del IoT (el backend lo lee con Mqtt__*)
Simulador__Habilitado=true dotnet run --project src/Vitalify.Api   # telemetría simulada, solo en Development y sin hardware

dotnet tool restore              # instala dotnet-ef 8 (herramienta local; la global puede ser otra versión)
dotnet ef database update --context TransaccionalDbContext --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef database update --context HistorialDbContext --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef migrations add <Nombre> --context TransaccionalDbContext -o Persistence/Transaccional/Migrations --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
dotnet ef migrations add <Nombre> --context HistorialDbContext -o Persistence/Historial/Migrations --project src/Vitalify.Infrastructure --startup-project src/Vitalify.Infrastructure
```

Las migraciones se aplican solo con `dotnet ef database update`. Nunca se aplican al arrancar la API.

## Arquitectura hexagonal

- `Vitalify.Domain`: C# puro, **sin paquetes NuGet ni referencias a otros proyectos**.
- `Vitalify.Application`: casos de uso y puertos (interfaces, en `Puertos/`). Depende **solo** del proyecto Domain; puede usar FluentValidation y `Microsoft.Extensions.DependencyInjection.Abstractions`, pero no EF Core, Npgsql ni ASP.NET.
- `Vitalify.Infrastructure`: adaptadores de salida (EF Core, repositorios, health checks). Depende de Application y Domain, **nunca de Api**. Expone `AddInfrastructure(IServiceCollection, IConfiguration)`.
- `Vitalify.Api`: adaptadores de entrada (controladores REST, Swagger) y raíz de composición (DI). Los controladores solo llaman a casos de uso; no usan Infrastructure ni EF Core (también lo verifica una prueba).

Convenciones de la capa de aplicación:
- Un caso de uso por clase, con `EjecutarAsync(comando, ct)`. Se inyecta en la acción con `[FromServices]` y se registra en `AddApplication()`.
- Devuelven `Resultado<T>` con un `Error` tipado (`Validacion`, `Conflicto`, `NoAutorizado`, `NoEncontrado`); no lanzan excepciones en el flujo normal. `Error.Codigo` sale como `type` del ProblemDetails (`ResultadoHttp.Problema`), así que es estable y el cliente lo usa.
- La validación de entrada es con FluentValidation y mensajes en español; las invariantes del dominio lanzan `ExcepcionDeDominio`.
- La hora siempre viene de `IReloj`, nunca de `DateTime.UtcNow`.

`tests/Vitalify.Architecture.Tests` verifica estas reglas con NetArchTest, con las referencias de cada ensamblado y leyendo el `.csproj` de Domain y Application. Si una regla falla, se corrige el diseño, no la prueba. Cada capa tiene una clase `AssemblyReference` que sirve de marcador para esas pruebas.

`Directory.Build.props` fija para todos los proyectos: net8.0, Nullable, ImplicitUsings, TreatWarningsAsErrors y `<Version>` (que `/api/v1/ping` expone). `global.json` fija el SDK 8.

## Persistencia: dos DbContext

Ambos viven en `src/Vitalify.Infrastructure/Persistence/`:

| Contexto | Esquema | Cadena | Contenido |
|---|---|---|---|
| `TransaccionalDbContext` | `vitalify` | `ConnectionStrings:Transaccional` | usuarios, pacientes, alertas (desde la fase 1) |
| `HistorialDbContext` | `vitalify_historial` | `ConnectionStrings:Historial` | `lectura_signos`, `estado_signos_actual` e `incidencia_telemetria` (fase 3); evaluaciones de riesgo en la fase 4 |

- Nunca usar el esquema `public`: Supabase lo expone por su API de datos.
- Cada contexto guarda su `__EFMigrationsHistory` en su propio esquema (`OpcionesNpgsql.Configurar`) y sus migraciones en `Persistence/<Contexto>/Migrations`.
- Las configuraciones de entidades (`IEntityTypeConfiguration`) se aplican por namespace: pon las de cada contexto bajo `Persistence.Transaccional` o `Persistence.Historial`.
- Tablas y columnas en snake_case mediante `ConvencionSnakeCase` (propia). **No usar EFCore.NamingConventions**: también renombra las columnas de `__EFMigrationsHistory` y rompe la tabla que ya existe en Supabase. Antes de aplicar una migración, revisar el SQL con `dotnet ef migrations script`.
- Migraciones de `TransaccionalDbContext`: `Inicial` (esquema), `Fase1_UsuariosYSesiones` (`usuario`, `sesion_refresco`, `token_revocado`, `auditoria`) y `Fase2_PacientesYSensores` (`cama`, `dispositivo`, `paciente`, `hospitalizacion`, `asignacion_dispositivo`).
- Un índice con nombre propio debe llevar `.HasDatabaseName(...)`. `HasIndex(expr, nombre)` solo nombra el índice en el modelo de EF: sin `HasDatabaseName`, la convención le pone el nombre por defecto y choca con otros índices sobre la misma columna.
- Los filtros de `HasFilter` son SQL literal: se escriben con los nombres de columna en snake_case (`estado = 'Activa'`, `liberado_en IS NULL`).
- Enums guardados como texto (`HasConversion<string>()`). Ninguna clave foránea borra en cascada los datos clínicos (`Restrict`).
- **`HistorialDbContext` pasará a TimescaleDB en la fase 7.** Solo cambiará su cadena de conexión y se agregará una migración. Por eso no debe usar nada propio de Supabase y los casos de uso deben acceder al historial por sus propios puertos (`IRepositorioLecturas`, `IRepositorioEstadoSignos`, `IRepositorioIncidencias`, `IUnidadDeTrabajoHistorial`).
  - No hay claves foráneas entre esquemas: el historial guarda `hospitalizacion_id`, `paciente_id` y `codigo_dispositivo` como valores.
  - `lectura_signos` tiene la clave `(codigo_dispositivo, medido_en)`, que incluye la columna de tiempo, como exige una hypertable.
  - Migraciones de `HistorialDbContext`: `Inicial`, `Fase3_Telemetria` y `Fase4_MotorClinico` (`observacion_enfermeria` y `evaluacion_riesgo`).
- `CadenaDeConexion.Obtener` lee la cadena y acepta tanto el formato clave=valor de Npgsql como una URI `postgresql://`, que convierte agregando `SSL Mode=Require`. En ambos formatos usa `Maximum Pool Size=5` si la cadena no lo fija. El pool es pequeño porque el pooler gratuito de Supabase admite pocas conexiones.
- `FabricasDeDiseno.cs` tiene los `IDesignTimeDbContextFactory` que usa `dotnet ef`. Cargan el `.env` igual que la API.
- `IUnidadDeTrabajo.GuardarCambiosAsync` traduce cada índice único a su `Conflicto` (tabla `ConflictosPorIndice` en `UnidadDeTrabajo`):
  - `ux_usuario_correo` → `correo-en-uso`;
  - `ux_hospitalizacion_cama_activa` → `cama-ocupada`;
  - `ux_hospitalizacion_paciente_activa` → `paciente-ya-hospitalizado`;
  - `ux_asignacion_dispositivo_vigente` → `dispositivo-ya-vinculado`;
  - `ux_asignacion_hospitalizacion_vigente` → `paciente-ya-tiene-dispositivo`;
  - además, los códigos únicos de cama, dispositivo y documento del paciente.

  Un índice único nuevo debe agregarse ahí. `DbUpdateConcurrencyException` se traduce a `conflicto-concurrencia`: `dispositivo` usa la columna de sistema `xmin` como token de concurrencia.

## Configuración y secretos

- **Nunca se escriben contraseñas ni cadenas de conexión en archivos versionados.** Van solo en `.env`, en la raíz, que está en `.gitignore` y `.dockerignore`. No usar `dotnet user-secrets` ni ponerlas en `appsettings*.json`. `.env.example` es la plantilla sin datos reales.
- `Program.cs` llama a `DotNetEnv.Env.NoClobber().TraversePath().Load()` antes de crear el builder: una variable de entorno ya definida gana sobre el `.env`. Las pruebas de integración dependen de esto para apuntar a su contenedor. Las variables `ConnectionStrings__X` se leen como `ConnectionStrings:X`. En Docker, `docker-compose.yml` pasa el mismo archivo con `env_file`.
- Variables del `.env`: `ConnectionStrings__Transaccional`, `ConnectionStrings__Historial`, `Jwt__Emisor`, `Jwt__Audiencia`, `Jwt__Clave` (32 o más caracteres; si no, la API no arranca), `Jwt__MinutosAccessToken`, `Jwt__MinutosInactividad`, `Jwt__HorasMaximasSesion`, `Seed__AdminCorreo`, `Seed__AdminNombre`, `Seed__AdminContrasena` y `Seed__DatosDemo` (`true` crea camas `MED-B-01` a `MED-B-06` y sensores `ESP32-001` a `ESP32-004`, solo en Development).
- Nunca escribir en el log contraseñas, tokens ni valores del `.env`.

## Autenticación y RBAC (fase 1)

- **Access token:** JWT HS256 de 15 min con los claims `sub`, `email`, `name`, `role` y `jti` (`ClaimsVitalify`). Se valida con `MapInboundClaims = false`, así que los claims se leen por esos nombres (`UsuarioAutenticado`).
- **Refresh token:** 64 bytes aleatorios. En `sesion_refresco` se guarda solo su SHA-256.
  - Cada refresh **rota** el token; la sesión nueva hereda `ExpiraEn` del login.
  - La sesión expira por inactividad (30 min sin refresh) o por duración máxima (12 h) y responde 401 con `sesion-expirada`.
  - Reusar un token ya rotado revoca todas las sesiones del usuario.
- **Logout:** guarda el `jti` en `token_revocado`. `OnTokenValidated` lo rechaza consultando `IRepositorioTokensRevocados`, que tiene un `IMemoryCache` delante.
- **Políticas** (`Api/Seguridad/Politicas.cs`): `SoloAdministrador`, `PersonalClinico`, `SoloMedico`, `SoloEnfermera` y `AdministradorOPersonalClinico` (para leer catálogos sin datos de pacientes).
  - La `FallbackPolicy` exige autenticación: un endpoint nuevo es privado salvo que lleve `[AllowAnonymous]`.
  - Los 403 se auditan como `AccesoDenegado` (`ManejadorResultadoAutorizacion`).
  - Al agregar módulos, actualizar `docs/matriz-permisos.md`.
- **Límite de login:** 5 intentos por minuto por IP (`RateLimit:LoginIntentosPorMinuto`); al superarlo, 429.
- **Administradores:** no se crean por la API. El primero lo crea `SemillaAdministrador` al arrancar, desde `Seed__*`, solo si no existe ninguno.

## Pacientes, camas y sensores (fase 2)

- **Modelo:**
  - `Cama` y `Dispositivo` forman el catálogo del administrador.
  - `Paciente` es la persona. Se identifica por tipo y número de documento y se reutiliza en cada reingreso.
  - `Hospitalizacion` es el episodio. Queda `Activa` o `Finalizada`.
  - `AsignacionDispositivo` guarda qué sensor estuvo con qué hospitalización y cuándo.
  - La ocupación de una cama se deriva de las hospitalizaciones activas; no se guarda.
- **Dispositivo:** tiene una máquina de estados (`Disponible`, `Asignado`, `Mantenimiento`, `DadoDeBaja`), cuyas transiciones están documentadas en la clase. Solo `AsignacionDispositivo.Vincular` y `Liberar` lo asignan o liberan, para que estado y asignación no queden en desacuerdo. `PATCH /dispositivos/{id}/estado` no puede tocar un dispositivo asignado.
- **Nada se borra:** no hay DELETE de pacientes ni hospitalizaciones. El egreso finaliza la hospitalización y, en el mismo `GuardarCambiosAsync`, libera el sensor.
- **Ley 29733:**
  - El administrador no ve datos de pacientes: en `GET /camas` ve si la cama está ocupada, no quién la ocupa.
  - El 409 de un sensor ocupado indica la cama, nunca el paciente.
  - La auditoría y los logs llevan solo ids, nunca nombres ni números de documento.
  - `GET /pacientes/{id}` se audita como `ConsultaFichaPaciente`.
- **Para la fase 3:** `ObtenerHospitalizacionActivaPorDispositivo(codigo)` resuelve qué hospitalización recibe la telemetría del tópico `device/{codigo}/telemetria`. No tiene endpoint.
- **Pendiente de la fase 4:** `ListarPacientesMonitoreados` devuelve `ultimoNews2` y `ultimoMews` en `null` y `nivelRiesgo = "sin-datos"`. Hay un `// TODO fase 4` donde se completan.
- **Edad:** se guarda la `FechaNacimiento`. Si solo se conoce la edad, se estima el 1 de enero del año correspondiente y se marca `FechaNacimientoEstimada`.

## Telemetría (fase 3)

- **Un único puerto de entrada: `IRegistrarLectura`** (caso de uso `RegistrarLectura`).
  - Lo llaman el endpoint `POST /api/v1/dev/telemetria` (solo Development, `SoloAdministrador`) y `SimuladorTelemetriaWorker`. En la fase 7 lo llamará el adaptador MQTT.
  - Los adaptadores viven en `src/Vitalify.Api/Entrada/` y nunca usan Infrastructure ni EF Core (lo verifica una prueba de arquitectura).
  - El JSON se interpreta con `MensajeTelemetria.Interpretar`, que todos los adaptadores comparten.
  - `origen` es solo trazabilidad: el núcleo no se comporta distinto según quién envió la lectura.
- **Contrato del firmware:** `docs/contrato-telemetria.md`. Cambiarlo es cambiar el firmware del ESP32.
- **Reglas** (en el dominio, puras):
  - `RangosFisiologicos`: rangos de plausibilidad, no clínicos.
  - `EvaluadorSignos`: descarte por variable; la presión es un par.
  - `VentanaDeRecepcion`: hasta 2 min en el futuro y 24 h de retraso.
  - `EstadoSignosActual.Aplicar`: cada variable solo avanza si su medición es más reciente.
  - `Vigencia`: `vigente`, `pendiente-actualizacion` o `sin-datos`, y la señal. Se calcula **al leer** con `IReloj`; no hay jobs.
- **Duplicados:** idempotencia por `(dispositivo, ts)`. Se revisa antes de guardar y, si hay una carrera, la clave primaria lo detecta y la unidad de trabajo lo traduce a `lectura-duplicada`, que el caso de uso devuelve como `Duplicada`.
- **Concurrencia en el estado:** `estado_signos_actual` usa `xmin` y `RegistrarLectura` reintenta hasta 3 veces si hay conflicto.
- **Caché del dispositivo:** `IResolutorDispositivos` recuerda durante 15 s a qué hospitalización corresponde cada dispositivo. `VincularDispositivo`, `LiberarDispositivo` y `RegistrarEgreso` llaman a `Invalidar`; si no lo hicieran, tras un re-vínculo las lecturas irían al paciente anterior.
- **Presión incompleta:** se llama a `ISolicitudNuevaLectura`. Hoy su adaptador solo escribe en el log; en la fase 7 publicará un comando MQTT.
- **Punto de extensión de la fase 4:** `IManejadorLecturaRegistrada` recibe el evento `LecturaRegistrada` después de guardar. Hoy no hay ninguno registrado; NEWS2/MEWS se engancha ahí, **no dentro de `RegistrarLectura`**.
- **Configuración:**
  - `Telemetria__SegundosVigencia` (90), `Telemetria__HorasMaximasRetraso` (24), `Telemetria__MinutosToleranciaFuturo` (2), `Telemetria__SegundosCacheDispositivo` (15) y `Telemetria__Rangos__{Fc|Fr|Spo2|Temperatura|Pas|Pad}__{Minimo|Maximo}`.
  - Simulador: `Simulador__Habilitado` (**false** por defecto), `Simulador__IntervaloSegundos` (10, mínimo 5 por el límite de 500 MB de Supabase), `Simulador__Semilla`, `Simulador__MinutosDeterioro`, `Simulador__LecturasEntreCaidas` y `Simulador__Escenarios__{codigo}` (`Estable`, `Deterioro`, `Caida` o `SensorDefectuoso`).
- **Datos personales:** el historial y las incidencias nunca guardan el nombre ni el documento del paciente. Las incidencias son del administrador y los signos, del personal clínico.

## IoT y motor clínico (fase 4)

- **Hardware real** (README del IoT):
  - ESP32-S3 con MAX30102 (FC y SpO2), AD8232 (ECG; la FC sale del ECG si es confiable), DS18B20 (temperatura de piel) y ADXL345 (caídas).
  - Publica en `device/{codigo}/telemetria`, con QoS 1, cada 10 s, y al instante si hay una caída. Usa un búfer offline de 1 h y publica `online`/`offline` (retenido, *last will*) en `device/{codigo}/estado`.
  - **No mide FR, PAS, PAD ni batería.**
- **`ReceptorMqtt`** (`Api/Entrada/Mqtt`):
  - Entra como `vitalify-backend`, con sesión persistente y client id fijo, y suscribe ambos tópicos.
  - Pasa la telemetría por `MensajeTelemetria` a `IRegistrarLectura` (origen `Mqtt`). El código del JSON debe coincidir con el del tópico.
  - El estado va a `RegistrarPresenciaDispositivo` → `IPresenciaDispositivos` (en memoria), que se ve como `conexionSensor` en monitoreados y en signos.
  - Si está habilitado, `/health` incluye `mqtt`.
  - Configuración: `Mqtt__Habilitado`, `Mqtt__Servidor`, `Mqtt__Puerto`, `Mqtt__Usuario` y `Mqtt__Clave` (= `MQTT_BACKEND_CLAVE` de `../IOT/tesis_V01/broker/.env`). Si la API corre en Docker, `docker-compose.yml` usa `host.docker.internal` como servidor.
- **Motor clínico** (`Domain/Clinica`, `Application/Clinica`, `docs/motor-clinico.md`):
  - `CalculadoraNews2` (RCP 2017) y `CalculadoraMews` (Subbe 2001) son tablas puras con pruebas en cada límite.
  - `EvaluarRiesgoAlRegistrarLectura` se engancha a `LecturaRegistrada`; las lecturas atrasadas no evalúan.
  - `EvaluadorRiesgo` toma por parámetro el valor más reciente entre el wearable (si está vigente) y la última `ObservacionEnfermeria` (dentro de `Clinico__HorasVigenciaObservacion`, 4 h).
  - Lo que falta suma 0 y queda en `faltantes`, con `completo: false`. **Un puntaje parcial es una cota inferior.**
  - `Clinico__AjusteTemperaturaSensor` (°C, 0 por defecto) corrige la temperatura de piel del DS18B20 antes de puntuar.
  - Las evaluaciones se guardan en `evaluacion_riesgo`; el desglose por parámetro se recalcula al leer.
  - Endpoints: `GET /pacientes/{id}/riesgo` y `POST /pacientes/{id}/observaciones` (`PersonalClinico`).
  - Las alertas por nivel medio o alto son de la fase 5.
- **Contrato con el firmware:** `ContratoFirmwareTests` usa los JSON exactos de `IOT/tesis_V01/tesis/pruebas/pruebas.cpp`. Si cambia el firmware, cambia esa prueba.

## Pruebas de integración

- `tests/Vitalify.Api.IntegrationTests` usa `WebApplicationFactory` y un PostgreSQL 17 en Testcontainers. `FabricaVitalify` pasa la configuración como variables de entorno y aplica las migraciones antes de arrancar la API.
- **Contenedores:**
  - Casi todas las clases comparten un contenedor (colección `api`). Usan correos, códigos y DNI únicos (`ClienteApi.NuevoDni()`) y no dependen del orden.
  - Las pruebas que necesitan partir de una base vacía usan la colección `api-aislada` (`FabricaVitalifyAislada`), que tiene su propio contenedor.
  - La colección `api-mqtt` (`FabricaVitalifyMqtt`) levanta además un Mosquitto real (`eclipse-mosquitto:2`, con anónimos permitidos). `PublicarAsync` publica como el ESP32 y `EsperarAsync` espera la ingesta asíncrona.
  - Las colecciones no corren en paralelo (`Infraestructura/Ensamblado.cs`), porque la configuración va por variables de entorno del proceso. Una fábrica derivada agrega contenedores y variables con `ConfiguracionAdicionalAsync`.
- **Atajos** en `ClienteApi`: `CrearCamaAsync`, `CrearDispositivoAsync`, `IngresarPacienteAsync`, `VincularAsync`, `EgresarAsync`, `RegistrarTelemetriaAsync` (entra por `/dev/telemetria`), `Ts` y `Ms`. En `PruebaApi`: `ClienteAdministradorAsync`, `ClienteEnfermeraAsync`, `ClienteMedicoAsync` y `PacienteConSensorAsync`.
- **Fábrica:**
  - `FabricaVitalify.ConsultarHistorialAsync` lee `HistorialDbContext`.
  - `SolicitudesNuevaLectura` registra las solicitudes de nueva lectura.
  - `FabricaVitalifyProduccion` (colección `api-produccion`) arranca en Production, para probar lo que solo existe en Development.
- **`tests/Vitalify.Api.Tests`:** pruebas unitarias de los adaptadores de entrada (determinismo del simulador; el worker solo llama al puerto).
- `RelojAjustable` reemplaza a `IReloj`: `using (Fabrica.Reloj.Adelantar(...))` y se restablece al salir del bloque.
- Cada `CrearCliente()` usa una IP distinta (encabezado `X-Ip-Prueba`), para que el límite de login no se comparta entre pruebas.
- Hay una prueba por escenario, con el nombre `HUxx_Ey_...`.

## Convenciones de la API

- Rutas versionadas: `api/v1/...`. Los controladores van en `Controllers/V1`.
- Errores: `ManejadorGlobalDeExcepciones` (`IExceptionHandler`) y `AddProblemDetails` con `traceId`. Todo error sale como ProblemDetails y el detalle y el stack trace solo se muestran en Development.
- `/health` usa los checks `transaccional` e `historial` (AspNetCore.HealthChecks.NpgSql, registrados en `AddInfrastructure`) y responde un JSON propio (`RespuestaHealth`).
- Swagger solo en Development, con comentarios XML y el botón Authorize. Cada endpoint lleva `[ProducesResponseType]` y comentarios XML.
- No poner `[Produces("application/json")]` en los controladores: sobrescribe el `application/problem+json` de los errores.
- Logs con `ILogger` en JSON por consola. Los orígenes de CORS se configuran en `Cors:OrigenesPermitidos` (appsettings).
- La configuración de la API está en `src/Vitalify.Api/Configuracion/`.
