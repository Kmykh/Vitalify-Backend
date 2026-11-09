# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Contexto

Backend de **Vitalify** (tesis UPC): plataforma IoMT hospitalaria. Un wearable ESP32 envía signos vitales, el backend calcula NEWS2 y MEWS y genera alertas para enfermería (app Flutter) y médicos (dashboard React). ASP.NET Core **.NET 8**, arquitectura hexagonal, PostgreSQL en **Supabase** usado solo como base de datos. No se usa Supabase Auth ni su SDK: el login será un JWT propio.

El código, los nombres del dominio y los mensajes de commit van en **español**.

## Commits

Se usa [Conventional Commits](https://www.conventionalcommits.org/es/) en español: `tipo(ámbito): descripción en presente y minúscula`. Los tipos son `feat`, `fix`, `test`, `docs`, `refactor`, `build`, `ci` y `chore`. Los ámbitos son la capa o el área afectada: `dominio`, `aplicacion`, `persistencia`, `api`, `arquitectura`, `docker`. Ejemplo: `feat(api): agrega el endpoint de login con JWT`. Se hace un commit por paso lógico, y el repositorio es público en GitHub (`Kmykh/Vitalify-Backend`).

## Fases

0 base · 1 usuarios, JWT y RBAC · 2 pacientes y sensores · 3 ingesta simulada · 4 NEWS2/MEWS con el wearable real · 5 vinculación por QR y toma manual · 6 alertas, turnos y notificaciones (SignalR y push) · 7 A: historial del paciente, notas clínicas y auditoría; B: producción con Supabase y Mosquitto con TLS · 8 proceso de enfermería con la taxonomía NNN.

Hechas:
- Fase 1 (HU01 a HU04): registro, login JWT, RBAC y cierre de sesión.
- Fase 2 (HU05, HU06, HU08 y la base de la HU07): pacientes, camas y sensores.
- Fase 3 (HU10, HU11 y HU12): ingesta de signos vitales con datos simulados.
- Fase 4: motor clínico NEWS2/MEWS, adaptado al hardware real. A pedido del usuario se adelantó de la fase 7 el **receptor MQTT**, para que el backend reciba lo que publica el wearable real (proyecto `../IOT/tesis_V01`, repositorio `Kmykh/IoT-Vitalfy`).
- Fase 5 (HU06 por QR, HU09 en los dos sentidos y HU12 con la toma manual): vinculación del wearable por QR, comandos MQTT al wearable, asistente de toma manual, `proximaTomaManualEn`, avisos y escala de SpO2 por paciente. Ver la sección de la fase 5.
- Adelantado de la fase 6 a pedido del usuario: `GET /pacientes/{id}/signos/historial`, para los gráficos del dashboard React de prueba en `../Web` (que envía `desde` y `hasta`). En la fase 7 pasó a las reglas del historial.
- Fase 6 (HU15, HU16, HU20, HU21 en el backend y HU23): servicios y turnos, alertas clínicas con escalamiento, SignalR, push por FCM y entrega de turno; además, el rechazo de lecturas anteriores a la vinculación y la vigencia de la toma según NEWS2. Ver la sección de la fase 6.
- Fase 7, parte A (HU19, HU22 y el backend de HU17 y HU18): historial del paciente para el médico, notas clínicas con versiones, consulta de la auditoría, monitoreados ordenados por riesgo y espera tras atender una alerta de riesgo (AL-E9). Ver la sección de la fase 7.
- Fase 7, parte B: producción con Supabase como única base y Mosquitto con TLS en el mismo servidor que la API (Caddy, retención del historial, almacenamiento, proxy). Ver la sección de producción y `docs/despliegue.md`.
- Fase 8: proceso de enfermería con la taxonomía NNN (NANDA-I, NIC y NOC): motor de sugerencias, plan de cuidados, ejecución de intervenciones, valoración NOC e indicadores para la tesis. Ver la sección de la fase 8 y `docs/nnn.md`.

**TimescaleDB ya no se usa**: el historial se queda en Supabase. **HiveMQ Cloud tampoco**: el broker es el Mosquitto del IoT, desplegado junto a la API. Los avisos de la fase 5 **no** crean alertas; la preocupación clínica sí (fase 6). **No usar datos simulados para verificar lo que ya puede verificarse con el wearable real.**

## Comandos

```bash
dotnet build                     # TreatWarningsAsErrors: cualquier warning rompe el build
dotnet test                      # las de integración necesitan Docker encendido (Testcontainers); ninguna usa Supabase
dotnet test --filter "FullyQualifiedName~ReglasDeDependencia"          # una clase de pruebas
dotnet test --filter "FullyQualifiedName~HU04"                         # los escenarios de una historia
dotnet test tests/Vitalify.Domain.Tests                                # un proyecto
dotnet run --project src/Vitalify.Api        # http://localhost:5080/swagger
docker compose up --build                    # http://localhost:8080
docker compose -f docker-compose.prod.yml up -d --build      # producción: api + caddy + mosquitto (DOMINIO en el .env; docs/despliegue.md)
DOMINIO=vitalify.localhost docker compose -f docker-compose.prod.yml up -d --build   # lo mismo en la laptop, con la CA local de Caddy
docker compose -f docker-compose.prod.yml config --quiet    # validar; sin --quiet imprime el .env, no usarlo

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
- Devuelven `Resultado<T>` con un `Error` tipado (`Validacion`, `Conflicto`, `NoAutorizado`, `NoEncontrado`, `Prohibido` → 403); no lanzan excepciones en el flujo normal. `Error.Codigo` sale como `type` del ProblemDetails (`ResultadoHttp.Problema`), así que es estable y el cliente lo usa.
- La validación de entrada es con FluentValidation y mensajes en español; las invariantes del dominio lanzan `ExcepcionDeDominio`.
- La hora siempre viene de `IReloj`, nunca de `DateTime.UtcNow`.
- Application no tiene `ILogger`: lo que falla sin cortar el flujo (un manejador, una notificación) va a `IBitacoraFallos`, solo con ids.

`tests/Vitalify.Architecture.Tests` verifica estas reglas con NetArchTest, con las referencias de cada ensamblado y leyendo el `.csproj` de Domain y Application. Si una regla falla, se corrige el diseño, no la prueba. Cada capa tiene una clase `AssemblyReference` que sirve de marcador para esas pruebas.

`Directory.Build.props` fija para todos los proyectos: net8.0, Nullable, ImplicitUsings, TreatWarningsAsErrors y `<Version>` (que `/api/v1/ping` expone). `global.json` fija el SDK 8.

## Persistencia: dos DbContext

Ambos viven en `src/Vitalify.Infrastructure/Persistence/`:

| Contexto | Esquema | Cadena | Contenido |
|---|---|---|---|
| `TransaccionalDbContext` | `vitalify` | `ConnectionStrings:Transaccional` | usuarios, pacientes, sensores, vinculación, servicios, turnos, alertas, tokens push, catálogo NNN y plan de cuidados |
| `HistorialDbContext` | `vitalify_historial` | `ConnectionStrings:Historial` | `lectura_signos`, `estado_signos_actual` e `incidencia_telemetria` (fase 3); `observacion_enfermeria` y `evaluacion_riesgo` (fases 4 y 5) |

- Nunca usar el esquema `public`: Supabase lo expone por su API de datos.
- Cada contexto guarda su `__EFMigrationsHistory` en su propio esquema (`OpcionesNpgsql.Configurar`) y sus migraciones en `Persistence/<Contexto>/Migrations`.
- Las configuraciones de entidades (`IEntityTypeConfiguration`) se aplican por namespace: pon las de cada contexto bajo `Persistence.Transaccional` o `Persistence.Historial`.
- Tablas y columnas en snake_case mediante `ConvencionSnakeCase` (propia). **No usar EFCore.NamingConventions**: también renombra las columnas de `__EFMigrationsHistory` y rompe la tabla que ya existe en Supabase. Antes de aplicar una migración, revisar el SQL con `dotnet ef migrations script`.
- Migraciones de `TransaccionalDbContext`: `Inicial` (esquema), `Fase1_UsuariosYSesiones` (`usuario`, `sesion_refresco`, `token_revocado`, `auditoria`), `Fase2_PacientesYSensores` (`cama`, `dispositivo`, `paciente`, `hospitalizacion`, `asignacion_dispositivo`) `Fase5_VinculacionQr` (`codigo_vinculacion`, `paciente.confusion_basal` y la escala de SpO2 en `hospitalizacion`; `escala_spo2` tiene default `'Escala1'` para las filas existentes), `Fase6_ServiciosYTurnos` (`servicio`, `programacion_turno`, `usuario.servicio_id` y `cama.servicio_id`; el SQL de movimiento de datos está escrito a mano: crea un servicio por cada texto de `cama.servicio` y su `Down` restaura la columna) `Fase6_Alertas` (`alerta_clinica` y `dispositivo_movil`), `Fase7_NotasClinicas` (`nota_clinica`, `nota_clinica_version` y los índices `(accion, fecha)` de `auditoria` y `(hospitalizacion_id, tipo, atendida_en)` de `alerta_clinica`) y `Fase8_ProcesoEnfermeria` (catálogo `nnn_diagnostico`, `nnn_intervencion`, `nnn_resultado`, `nnn_indicador` y `nnn_vinculo`; plan `plan_diagnostico`, `plan_intervencion`, `plan_intervencion_ejecucion`, `plan_resultado`, `plan_resultado_valoracion` y `plan_sugerencia`; solo `CREATE`).
- Un índice con nombre propio debe llevar `.HasDatabaseName(...)`. `HasIndex(expr, nombre)` solo nombra el índice en el modelo de EF: sin `HasDatabaseName`, la convención le pone el nombre por defecto y choca con otros índices sobre la misma columna.
- Los filtros de `HasFilter` son SQL literal: se escriben con los nombres de columna en snake_case (`estado = 'Activa'`, `liberado_en IS NULL`).
- Enums guardados como texto (`HasConversion<string>()`). Ninguna clave foránea borra en cascada los datos clínicos (`Restrict`).
- **`HistorialDbContext` se queda en Supabase** (decisión de la fase 7: TimescaleDB ya no se usa). Podría pasar a otra base, por ejemplo TimescaleDB, cambiando solo `ConnectionStrings__Historial`. Por eso no debe usar nada propio de Supabase y los casos de uso deben acceder al historial por sus propios puertos (`IRepositorioLecturas`, `IRepositorioEstadoSignos`, `IRepositorioIncidencias`, `IUnidadDeTrabajoHistorial`).
  - No hay claves foráneas entre esquemas: el historial guarda `hospitalizacion_id`, `paciente_id` y `codigo_dispositivo` como valores.
  - `lectura_signos` tiene la clave `(codigo_dispositivo, medido_en)`, que incluye la columna de tiempo (como exigiría una hypertable).
  - Migraciones de `HistorialDbContext`: `Inicial`, `Fase3_Telemetria`, `Fase4_MotorClinico` (`observacion_enfermeria` y `evaluacion_riesgo`) `Fase5_TomaManual` (columnas opcionales del asistente; `checklist_pa`, `lecturas_pa` y `contexto` son `jsonb` con `ConversoresJson`) `Fase6_VigenciaToma` (`observacion_enfermeria.vigente_hasta` y `evaluacion_riesgo.proxima_toma_manual_en`) y `Fase7_Retencion` (`lectura_signos_resumen`, clave `(hospitalizacion_id, inicio)`; promedios `numeric` sin escala).
- `CadenaDeConexion.Obtener` lee la cadena y acepta tanto el formato clave=valor de Npgsql como una URI `postgresql://`, que convierte agregando `SSL Mode=Require`. En ambos formatos usa `Maximum Pool Size=5` si la cadena no lo fija. El pool es pequeño porque el pooler gratuito de Supabase admite pocas conexiones. Agrega `Application Name` `vitalify-transaccional` o `vitalify-historial` (un pool por contexto) y `ValidarPresupuesto` impide arrancar si las dos cadenas apuntan a la misma base y sus pools suman más de 10.
- SQL a mano (ADO.NET con parámetros, `ConsultasAgregadas`): agregaciones, retención y `MedidorAlmacenamiento`. Las tablas y columnas salen del modelo de EF.
- `FabricasDeDiseno.cs` tiene los `IDesignTimeDbContextFactory` que usa `dotnet ef`. Cargan el `.env` igual que la API.
- `IUnidadDeTrabajo.GuardarCambiosAsync` traduce cada índice único a su `Conflicto` (tabla `ConflictosPorIndice` en `UnidadDeTrabajo`):
  - `ux_usuario_correo` → `correo-en-uso`;
  - `ux_hospitalizacion_cama_activa` → `cama-ocupada`;
  - `ux_hospitalizacion_paciente_activa` → `paciente-ya-hospitalizado`;
  - `ux_asignacion_dispositivo_vigente` → `dispositivo-ya-vinculado`;
  - `ux_asignacion_hospitalizacion_vigente` → `paciente-ya-tiene-dispositivo`;
  - `ux_codigo_vinculacion_activo` → `codigo-vinculacion-concurrente`;
  - `ux_servicio_codigo` → `servicio-codigo-duplicado`;
  - `ux_alerta_abierta` → `alerta-abierta-duplicada` (`ServicioAlertas` reintenta una vez y actualiza la que ganó);
  - `ux_programacion_turno` → `programacion-concurrente`;
  - `ux_dispositivo_movil_token` → `token-push-concurrente`;
  - `ux_nota_clinica_version` → `nota-editada-concurrente`;
  - `ux_plan_diagnostico_activo` → `diagnostico-ya-activo`;
  - `ux_plan_intervencion_activa` → `intervencion-ya-planificada`;
  - `ux_plan_resultado` → `resultado-ya-planificado`;
  - `ux_plan_sugerencia_pendiente` → `sugerencia-concurrente` (`ServicioSugerenciasNnn` lo ignora: otra sincronización ya la registró);
  - además, los códigos únicos de cama, dispositivo y documento del paciente.

  Un índice único nuevo debe agregarse ahí. `DbUpdateConcurrencyException` se traduce a `conflicto-concurrencia`: `dispositivo`, `estado_signos_actual` y `nota_clinica` usan la columna de sistema `xmin` como token de concurrencia.

## Configuración y secretos

- **Nunca se escriben contraseñas ni cadenas de conexión en archivos versionados.** Van solo en `.env`, en la raíz, que está en `.gitignore` y `.dockerignore`. No usar `dotnet user-secrets` ni ponerlas en `appsettings*.json`. `.env.example` es la plantilla sin datos reales.
- `Program.cs` llama a `DotNetEnv.Env.NoClobber().TraversePath().Load()` antes de crear el builder: una variable de entorno ya definida gana sobre el `.env`. Las pruebas de integración dependen de esto para apuntar a su contenedor. Las variables `ConnectionStrings__X` se leen como `ConnectionStrings:X`. En Docker, `docker-compose.yml` pasa el mismo archivo con `env_file`.
- Variables del `.env`: `ConnectionStrings__Transaccional`, `ConnectionStrings__Historial`, `Jwt__Emisor`, `Jwt__Audiencia`, `Jwt__Clave` (32 o más caracteres; si no, la API no arranca), `Jwt__MinutosAccessToken`, `Jwt__MinutosInactividad`, `Jwt__HorasMaximasSesion`, `Seed__AdminCorreo`, `Seed__AdminNombre`, `Seed__AdminContrasena` y `Seed__DatosDemo` (`true` crea el servicio `MEDICINA-B`, camas `MED-B-01` a `MED-B-06` y sensores `ESP32-001` a `ESP32-004`, solo en Development). Las de la fase 6 (`Turnos__*`, `Alertas__*`, `Clinico__MinutosToleranciaToma`, `Push__*`) y las de producción (`DOMINIO`, `Cors__Origenes`, `Proxy__RedesConocidas`, `Swagger__Habilitado`, `Mqtt__UsarTls`, `Mqtt__ValidarCertificado`, `Historial__*`, `Sistema__LimiteAlmacenamientoMb`) están en `.env.example`.
- **Las claves MQTT de producción** van en `deploy/mosquitto/secretos/passwd` (hash de `mosquitto_passwd`), que está en `.gitignore` y `.dockerignore`. Nunca en el repositorio.
- **El JSON de la cuenta de servicio de Firebase nunca va al repositorio**: `Push__RutaCredencialesFirebase` apunta fuera de él y el `.gitignore` excluye `*firebase*.json`, `*-firebase-adminsdk-*.json` y `credenciales/`.
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
  - `Cama` y `Dispositivo` forman el catálogo del administrador. Cada cama pertenece a un `Servicio` (`ServicioId`, fase 6).
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
  - Lo llaman el endpoint `POST /api/v1/dev/telemetria` (solo Development, `SoloAdministrador`), `SimuladorTelemetriaWorker` y `ReceptorMqtt`.
  - Los adaptadores viven en `src/Vitalify.Api/Entrada/` y nunca usan Infrastructure ni EF Core (lo verifica una prueba de arquitectura).
  - El JSON se interpreta con `MensajeTelemetria.Interpretar`, que todos los adaptadores comparten.
  - `origen` es solo trazabilidad: el núcleo no se comporta distinto según quién envió la lectura.
- **Contrato del firmware:** `docs/contrato-telemetria.md`. Cambiarlo es cambiar el firmware del ESP32.
- **Reglas** (en el dominio, puras):
  - `RangosFisiologicos`: rangos de plausibilidad, no clínicos.
  - `EvaluadorSignos`: descarte por variable; la presión es un par.
  - `VentanaDeRecepcion`: hasta 2 min en el futuro y 24 h de retraso.
  - Una lectura con `medidoEn` anterior al `asignado_en` de la vinculación vigente se rechaza con `AnteriorAVinculacion` (fase 6), antes de revisar duplicados. Por eso `PacienteConSensorAsync` vincula 6 h antes en las pruebas.
  - `EstadoSignosActual.Aplicar`: cada variable solo avanza si su medición es más reciente.
  - `Vigencia`: `vigente`, `pendiente-actualizacion` o `sin-datos`, y la señal. Se calcula **al leer** con `IReloj`; no hay jobs.
- **Duplicados:** idempotencia por `(dispositivo, ts)`. Se revisa antes de guardar y, si hay una carrera, la clave primaria lo detecta y la unidad de trabajo lo traduce a `lectura-duplicada`, que el caso de uso devuelve como `Duplicada`.
- **Concurrencia en el estado:** `estado_signos_actual` usa `xmin` y `RegistrarLectura` reintenta hasta 3 veces si hay conflicto.
- **Caché del dispositivo:** `IResolutorDispositivos` recuerda durante 15 s a qué hospitalización corresponde cada dispositivo. `VincularDispositivo`, `LiberarDispositivo` y `RegistrarEgreso` llaman a `Invalidar`; si no lo hicieran, tras un re-vínculo las lecturas irían al paciente anterior.
- **Presión incompleta:** se llama a `ISolicitudNuevaLectura`, cuyo adaptador (`SolicitudNuevaLecturaPorComando`) publica `repetir-medicion` al wearable.
- **Punto de extensión:** `IManejadorLecturaRegistrada` recibe el evento `LecturaRegistrada` después de guardar: `EvaluarRiesgoAlRegistrarLectura` (fase 4), `AlertasAlRegistrarLectura` y `PublicarSignosEnTiempoReal` (fase 6). Todo lo nuevo se engancha ahí, **no dentro de `RegistrarLectura`**, que atrapa el fallo de cada manejador y lo manda a `IBitacoraFallos`.
- **Configuración:**
  - `Telemetria__SegundosVigencia` (90), `Telemetria__HorasMaximasRetraso` (24), `Telemetria__MinutosToleranciaFuturo` (2), `Telemetria__SegundosCacheDispositivo` (15) y `Telemetria__Rangos__{Fc|Fr|Spo2|Temperatura|Pas|Pad}__{Minimo|Maximo}`.
  - Simulador: `Simulador__Habilitado` (**false** por defecto), `Simulador__IntervaloSegundos` (10, mínimo 5 por el límite de 500 MB de Supabase), `Simulador__Semilla`, `Simulador__MinutosDeterioro`, `Simulador__LecturasEntreCaidas` y `Simulador__Escenarios__{codigo}` (`Estable`, `Deterioro`, `Caida` o `SensorDefectuoso`).
- **Datos personales:** el historial y las incidencias nunca guardan el nombre ni el documento del paciente. Las incidencias son del administrador y los signos, del personal clínico.

## IoT y motor clínico (fase 4)

- **Hardware real** (README del IoT):
  - ESP32 (DevKit en la placa; también ESP32-S3) con MAX30102 (FC y SpO2), AD8232 (ECG; la FC sale del ECG si es confiable), DS18B20 (temperatura de piel) y MPU6050 sobre el tórax (caídas y FR; el firmware acepta un ADXL345 si no hay MPU6050).
  - Publica en `device/{codigo}/telemetria`, con QoS 1, cada 10 s, y al instante si hay una caída. Usa un búfer offline de 1 h y publica `online`/`offline` (retenido, *last will*) en `device/{codigo}/estado`.
  - **Mide la FR** con el MPU6050 (llega en `fr`, solo con el paciente quieto; `respiracion.h` del firmware). **No mide PAS, PAD ni batería.**
  - El firmware sigue los comandos de vinculación (`vinculacion.h`): muestra el QR, no envía telemetría hasta `vinculado` y descarta el búfer al desvincularse.
  - En producción, `MQTT_BROKER_TLS` en `secretos.h` lo conecta al dominio por el 8883 con TLS, confiando en ISRG Root X1 y X2 (`raices_tls.h`) y después de tener hora NTP. Sin esa definición usa el broker de la laptop (1883).
- **`ReceptorMqtt`** (`Api/Entrada/Mqtt`):
  - Entra como `vitalify-backend`, con sesión persistente y client id fijo, y suscribe `telemetria`, `estado` y `solicitudes`. Es la **única conexión MQTT**: también publica los comandos de `ColaComandosMqtt` (no abrir otra).
  - Pasa la telemetría por `MensajeTelemetria` a `IRegistrarLectura` (origen `Mqtt`). El código del JSON debe coincidir con el del tópico.
  - El estado va a `RegistrarPresenciaDispositivo` → `IPresenciaDispositivos` (en memoria), que se ve como `conexionSensor` en monitoreados y en signos.
  - Si está habilitado, `/health` incluye `mqtt`.
  - Configuración: `Mqtt__Habilitado`, `Mqtt__Servidor`, `Mqtt__Puerto`, `Mqtt__Usuario` y `Mqtt__Clave` (= `MQTT_BACKEND_CLAVE` de `../IOT/tesis_V01/broker/.env`). Si la API corre en Docker, `docker-compose.yml` usa `host.docker.internal` como servidor.
  - TLS (fase 7): `Mqtt__UsarTls` (false) usa `WithTlsOptions` con el servidor como SNI. `Mqtt__ValidarCertificado` (true) solo puede ser false en Development: `OpcionesMqtt.Cargar(configuration, esDesarrollo)` falla si no. El chequeo `mqtt` de `/health` describe el broker (host, puerto, TLS), nunca el usuario ni la clave.
- **Motor clínico** (`Domain/Clinica`, `Application/Clinica`, `docs/motor-clinico.md`):
  - `CalculadoraNews2` (RCP 2017) y `CalculadoraMews` (Subbe 2001) son tablas puras con pruebas en cada límite.
  - `EvaluarRiesgoAlRegistrarLectura` se engancha a `LecturaRegistrada`; las lecturas atrasadas no evalúan.
  - `EvaluadorRiesgo` toma por parámetro el valor más reciente entre el wearable (si está vigente) y la última `ObservacionEnfermeria` mientras cuente (`FrecuenciaTomaManual.Cuenta`: hasta su próxima toma más `Clinico__MinutosToleranciaToma`, 30 min, con tope de 12 h; una evaluación posterior que empeora acorta el plazo).
  - Lo que falta suma 0 y queda en `faltantes`, con `completo: false`. **Un puntaje parcial es una cota inferior.**
  - `Clinico__AjusteTemperaturaSensor` (°C, 0 por defecto) corrige la temperatura de piel del DS18B20 antes de puntuar.
  - Las evaluaciones se guardan en `evaluacion_riesgo`; el desglose por parámetro se recalcula al leer.
  - Fase 7: las del wearable solo se guardan si `EvaluacionRiesgo.AportaRespectoDe(anterior)` (cambio de total, nivel o completitud de NEWS2 o MEWS, de la próxima toma, o 5 min). Las de toma manual o cambio de escala, siempre. `RiesgoEvaluado` se publica con cada evaluación, guardada o no (`EvaluacionId` = la última guardada); si no, AL-E9 se reabriría tarde.
  - Endpoints: `GET /pacientes/{id}/riesgo` y `POST /pacientes/{id}/observaciones` (`PersonalClinico`).
- **Contrato con el firmware:** `ContratoFirmwareTests` usa los JSON exactos de `IOT/tesis_V01/tesis/pruebas/pruebas.cpp`. Si cambia el firmware, cambia esa prueba.

## Vinculación por QR y toma manual (fase 5)

- **Docs:** `docs/vinculacion-qr.md` (flujo, formato y seguridad), `docs/contrato-telemetria.md` (sección "Comandos al wearable" y lo que debe hacer el firmware) y `docs/motor-clinico.md` (asistente, próxima toma y avisos).
- **QR:**
  - Formato `VTF1:{codigoDispositivo}:{codigo}`, con 8 caracteres de `CodigoVinculacion.Alfabeto`.
  - En `codigo_vinculacion` solo se guardan los SHA-256 del código y del token, comparados con `FixedTimeEquals`.
  - Vale 10 min; el token, 5 min. Al 5.º intento fallido, el código se invalida y se publica otro.
  - Vencido, usado o inventado → siempre el mismo 404 `qr-no-valido`.
- **Comandos:**
  - `IPublicadorComandosDispositivo`: el adaptador es `PublicadorComandosMqtt` (Api/Salida/Mqtt, cola + `ReceptorMqtt`), o `PublicadorComandosEnLog` con MQTT apagado.
  - El JSON está solo en `ContratoComandos` (UTF-8 sin escapar).
  - Retenidos: `mostrar-qr`, `vinculado` y `desvinculado`. Al wearable solo van la cama y las iniciales (`Paciente.CalcularIniciales`).
- **Quién publica qué:**
  - `ServicioCodigosVinculacion` es el único que genera códigos y publica la pantalla.
  - Al pasar a `online`, `RegistrarPresenciaDispositivo` (ahora scoped y asíncrono) llama a `SincronizarPantallaDispositivo`.
  - `VincularDispositivo`, `LiberarDispositivo` y `RegistrarEgreso` publican `vinculado` o `desvinculado` + QR nuevo.
  - `RenovadorCodigosVinculacionWorker` (`Vinculacion__SegundosRenovacion`, 60; 0 lo apaga) renueva los QR vencidos.
- **`ConfirmarVinculacion`:** reutiliza `RegistrarIngresoPaciente.PrepararAsync`, `VincularDispositivo.PrepararAsync` y `ServicioObservaciones`; no duplica reglas. El orden está documentado en la clase: validar, una transacción y después el historial. Si el historial falla, responde 201 con aviso; el adaptador registra la excepción en el log, porque Application no tiene `ILogger`.
- **Toma manual:**
  - `TomaManualComando` + `TomaManualValidador` (o `TomaInicialValidador` en la vinculación).
  - El ACVPU lo calcula `RespuestasConciencia` (`NivelConciencia` es un enum: no puede tener `DesdeRespuestas`).
  - `TomaPresion` promedia y exige una 3.ª lectura si dos difieren en más de 10 mmHg.
  - `FrecuenciaTomaManual` calcula `proximaTomaManualEn` y `AvisosClinicos` genera los avisos al leer.
  - La guía es un recurso embebido de Application (`Clinica/Guias/toma-manual.json`, `GuiaTomaManual`). Cambiarla es subir su `version`.
- **Escala de SpO2:**
  - Va en `Hospitalizacion.EscalaSpo2` y viaja en `HospitalizacionPorDispositivoDto` y `LecturaRegistrada` hasta `EvaluadorRiesgo.EvaluarAsync(..., escala)`.
  - Cambiarla invalida la caché del resolutor.
- **Validación:** los errores de campos anidados salen como `presion.lecturas[0].pas` (`Validacion.CamelCase` por segmento).

## Alertas, turnos y notificaciones (fase 6)

- **Doc:** `docs/alertas-y-turnos.md` (disparadores, escalamiento, eventos, push, entrega de turno y cómo probarlo); permisos en `docs/matriz-permisos.md`.
- **Servicios y turnos** (`Domain/Servicios`, `Domain/Turnos`):
  - Enfermeras y médicos nuevos llevan `servicioId` obligatorio.
  - `CatalogoTurnos` convierte (fecha, tipo) en un intervalo UTC `[inicio, fin)` con `Turnos__ZonaHoraria` (America/Lima, con UTC−5 fijo de respaldo). Una nocturna pertenece al día en que empieza.
  - `PlanillaMensual.Validar` devuelve los errores con claves `dias.{d}`, `dias.{d}.{letra}` y `letras.{letra}`.
  - `ITurnoVigente` (`TurnoVigenteEnBase`) decide quién está de turno. Si nadie está programado, usa como respaldo las enfermeras activas del servicio (o todas) y escribe una advertencia en el log, solo con el id.
- **Alertas** (`Domain/Alertas`, `Application/Alertas`):
  - `AlertaClinica` tiene una alerta abierta por (hospitalización, tipo) (`ux_alerta_abierta`). `Actualizar` devuelve si subió, y la prioridad nunca baja.
  - Solo `SinSenal` y `TomaPendiente` se cierran solas (`ResolverAutomaticamente`); **no cerrar solas `riesgo`, `caida` ni `preocupacion-clinica`**.
  - `ServicioAlertas.DispararAsync` es el único que crea o actualiza alertas. `DestinatariosAlertas` decide a quién se avisa y `NotificadorAlertas` envía tiempo real y push y guarda `notificada_a`.
  - Disparadores:
    - `RiesgoEvaluado` (evento nuevo, lo publican `EvaluarRiesgoAlRegistrarLectura` y `ServicioObservaciones`) → `AlertaDeRiesgoAlEvaluar` y `PublicarRiesgoEnTiempoReal`;
    - `LecturaRegistrada` → `AlertasAlRegistrarLectura` (caída y cierre de sin señal) y `PublicarSignosEnTiempoReal` (`LimitadorSignosTiempoReal`, uno cada 5 s por paciente);
    - `VigilarAlertas` lo corre `VigilanteAlertasWorker` cada `Alertas__SegundosVigilancia` (30; 0 lo apaga) y revisa sin señal, tomas vencidas, escalamiento y cambio de turno.
  - Un fallo al notificar nunca rompe la ingesta.
  - `TomarAlerta`, `AtenderAlerta`, `ListarAlertas` y `ListarAlertasPaciente` exigen que el usuario sea del servicio de la alerta (`AccesoServicio`; 403 `usuario-sin-servicio` o `alerta-de-otro-servicio`).
- **Tiempo real** (`Api/TiempoReal/HubMonitoreo.cs`):
  - Hub `/hubs/monitoreo`, `PersonalClinico`. `AutenticacionJwt` acepta `access_token` en el query **solo** en esa ruta.
  - Grupos `servicio:{id}`, `usuario:{id}` y `medicos:{servicioId}`. Los eventos van al grupo del servicio, con `destinatarios` y `motivo` (`nueva`, `subio`, `escalamiento`, `cambio-turno` o `estado`, que no hace sonar).
  - `ConexionesMonitoreo` registra si cada destinatario estaba conectado.
  - El puerto es `INotificadorTiempoReal` (Application). Domain y Application no dependen de SignalR ni de FirebaseAdmin (lo verifica `ReglasDeDependencia`).
- **Push** (`Infrastructure/Notificaciones`):
  - `NotificadorPushEnLog` por defecto; `NotificadorPushFcm` con `Push__Habilitado=true`.
  - Los tokens están en `dispositivo_movil` (con un hash único) y se borran si FCM responde `no-registrado`.
  - Los textos están en `TextosPush`.
- **Datos personales:** SignalR, push, logs, auditoría y `detalle` de la alerta llevan solo ids, cama y puntajes. Solo `entrega-turno` (REST, personal clínico) trae el nombre.
- **Auditoría:** `ProgramacionCargada`, `ServicioAsignado`, `AlertaTomada` y `AlertaAtendida`.
- **Espera tras atender (fase 7):** `ServicioAlertas` no abre otra alerta de `riesgo` durante `Alertas__MinutosEsperaTrasAtender` (30; 0 la desactiva) después de atender una, salvo que suba la prioridad o el nivel (`AlertaClinica.SilenciaNuevaAlerta`, `IRepositorioAlertas.ObtenerUltimaAtendidaAsync`).

## Historial del paciente, notas y auditoría (fase 7, parte A)

- **Doc:** `docs/historial-clinico.md` (reglas, endpoints, notas, auditoría y lo que queda fuera de alcance).
- **Episodio y rango** (`Application/Historial`):
  - `EpisodiosPaciente.ElegirAsync` elige el episodio: `hospitalizacionId` (debe ser del paciente, si no 404 `hospitalizacion-no-encontrada`) o la activa, o la última (`paciente-sin-hospitalizaciones` si nunca estuvo).
  - `VentanaHistorial` (dominio) resuelve el rango: por defecto el episodio completo; sin `agrupar`, máximo 7 días (400; sin rango, el error va en `agrupar`); agrupado, el episodio completo.
  - Las observaciones no tienen tope y empiezan `FrecuenciaTomaManual.TopeVigencia` (12 h) antes del ingreso, porque una toma puede registrarse con su hora real.
- **Agrupar** (`IntervaloAgrupacion`): signos `1m|5m|15m|1h`, riesgo `5m|15m|1h`. Se agrega en SQL con `ConsultasAgregadas` (`floor(epoch / segundos)`, ADO.NET con parámetros; tablas y columnas salen del modelo de EF). EF 8 no traduce `date_bin`. Es PostgreSQL estándar (valdría también en TimescaleDB).
- **Acceso y auditoría:**
  - El historial lo lee `PersonalClinico` de **cualquier** servicio.
  - `AuditoriaHistorial` registra cada consulta (hospitalizaciones, signos, riesgo, observaciones, resumen, alertas del paciente, notas y versiones) como `ConsultaHistorialPaciente`, con ids.
  - `GET /pacientes/{id}/alertas` dejó de filtrar por servicio y es por episodio.
- **Resumen:** extremos del sensor y de la toma manual por separado (la temperatura del sensor es de piel); alertas por tipo con tiempos medios (el de atendida, solo las que cerró una persona).
- **Notas** (`Domain/Notas`, `Application/Notas`):
  - `NotaClinica` con `Version` y `xmin`. `Editar` devuelve la `NotaClinicaVersion` anterior (null si no cambió nada).
  - Solo el autor corrige (403 `nota-de-otro-autor`) y solo dentro de las 24 h desde la creación (409 `nota-fuera-de-plazo`).
  - No hay DELETE. Se audita `NotaRegistrada` y `NotaEditada`, sin el texto.
- **Auditoría para el administrador** (`Application/Auditoria`): `AuditoriaDto` trae acción, usuario, fecha, IP y `referencias` (los GUID del detalle). **Nunca el texto del detalle**: puede llevar el motivo de egreso o el correo de un login fallido.
- **HU18:** `ListarPacientesMonitoreados` ordena por nivel NEWS2, totales de NEWS2 y MEWS, alerta abierta más grave y cama.
- **Fuera de alcance:** umbrales de alerta por paciente (NEWS2 y MEWS son escalas estandarizadas; lo personalizable es la escala de SpO2).

## Producción (fase 7, parte B)

- **Doc:** `docs/despliegue.md` (paso a paso en una VM Linux, DuckDNS, claves MQTT, cron del certificado, operación) y la sección "Firmware en producción" de `docs/contrato-telemetria.md`.
- **`docker-compose.prod.yml`:** `api` (sin puertos, `env_file: .env`, Production), `caddy` (80/443, `deploy/caddy/Caddyfile`: `{$DOMINIO}` → `api:8080`) y `mosquitto` (8883 publicado; 1883 solo en la red interna `172.30.57.0/24`). Sin PostgreSQL: la base es Supabase.
- **Mosquitto** (`deploy/mosquitto`): `iniciar.sh` espera el certificado de Caddy (volumen `caddy_datos` en solo lectura), lo copia con `copiar-cert.sh` a `/mosquitto/certs` con permisos del usuario `mosquitto` (Mosquitto 2 suelta los privilegios antes de cargarlo) y exige `secretos/passwd`. El ACL es el del IoT más `solicitudes`. `scripts/recargar-cert-mosquitto.sh` (cron semanal) copia y manda `SIGHUP`; `scripts/agregar-wearable-mqtt.sh CODIGO` crea la clave de un wearable.
- **Proxy** (`Configuracion/ProxyVitalify.cs`): `UseForwardedHeaders` es lo primero del pipeline (X-Forwarded-For y X-Forwarded-Proto, `ForwardLimit=1`); solo desde `Proxy__RedesConocidas` (CIDR separados por comas; sin configurar, ningún proxy). Fuera de Development: `UseHsts` (365 días) y `UseHttpsRedirection` al 443.
- **CORS** (`CorsVitalify`): `Cors:Origenes` (lista en appsettings.Development o `Cors__Origenes` con comas; `*` no arranca). La API REST no admite credenciales; el hub usa `PoliticaSignalR` con `AllowCredentials` (el cliente JS de SignalR negocia con `withCredentials`).
- **SignalR:** KeepAlive 15 s, ClientTimeout 30 s, WebSockets, SSE y long polling (`MapTiempoReal`).
- **Solo Development:** el simulador ni se registra fuera de Development (aunque `Simulador__Habilitado=true`, que se avisa en el log), `POST /dev/telemetria` y Swagger (salvo `Swagger__Habilitado=true`).
- **Retención** (`Application/Historial/Retencion.cs`, `Domain/Historial/RetencionHistorial.cs`): `RetencionHistorialWorker` corre a las 03:00 de `Turnos__ZonaHoraria` (`ProgramaDiario`) y llama a `ResumirLecturasAntiguas`. Resume las crudas de los episodios **egresados** hace más de `Historial__DiasRetencionCrudo` (30) días en `lectura_signos_resumen` (5 min) y las borra en la misma sentencia (CTE `DELETE ... RETURNING` + `INSERT ... ON CONFLICT`), un día por sentencia. Es la única excepción a "no borrar". `AgruparAsync` y `ObtenerExtremosAsync` unen crudas y resumidas; `HistorialSignosDto.ResumidoHasta` y `GrupoSignosDto.Resumido` lo indican.
- **Almacenamiento:** `GET /api/v1/sistema/almacenamiento` (`SoloAdministrador`, `IMedidorAlmacenamiento`): `pg_database_size` y `pg_total_relation_size` por tabla de los dos esquemas, con el límite `Sistema__LimiteAlmacenamientoMb` (500).
- Supabase pausa un proyecto gratuito tras 7 días sin actividad (anotado en el README).

## Proceso de enfermería NNN (fase 8)

- **Doc:** `docs/nnn.md` (licencia, ediciones, verificación, reglas con su fundamento, flujo, endpoints e indicadores); permisos en `docs/matriz-permisos.md`. **Vitalify sugiere; la enfermera decide**: nada entra al plan sin que una enfermera lo confirme.
- **Base normativa (verificada):** Ley 27669, art. 7 a) (cuidado basado en el PAE) y su reglamento, D.S. 004-2002-SA, art. 9 a) (valoración, diagnóstico, planificación, ejecución y evaluación, registrados en la historia clínica). NTS 139: no se borra.
- **Catálogo** (`Domain/Nnn/CatalogoNnn.cs`):
  - Solo código, etiqueta corta y edición (`NANDA-I 2024-2026`, `NIC 8a ed.`, `NOC 7a ed.`). **Nunca** definiciones, características ni actividades: el repo es público y NANDA-I/NIC/NOC tienen derechos de autor.
  - Todo lo sembrado queda `verificado = false` hasta que el administrador lo marca (`PUT /nnn/catalogo/{tipo}/{codigo}/verificacion`).
  - `SemillaCatalogoNnn` carga `Semillas/nnn-catalogo.2026.11.1.json` (recurso embebido) al arrancar, en todos los entornos. Es idempotente: agrega lo que falta, corrige lo no verificado, nunca toca lo verificado ni borra. El log lleva solo cantidades.
- **Motor** (`Domain/Nnn/MotorSugerenciasNnn.cs`):
  - Puro, versión `2026.11.1`. Reglas R1 a R10 más R11 (NIC 7710 si NEWS2 ≥ 5 o algún parámetro en 3).
  - Los umbrales son los **puntos NEWS2**, no números nuevos, y van en código, no en `appsettings`.
  - Para agregar una regla: id nuevo, prueba en `tests/Vitalify.Domain.Tests/Fase8` y subir `VersionReglas`.
  - `ValoracionNocSugerida`: 3 puntos → 1, 2 → 2, 1 → 3, 0 → 5; el 4 lo elige la enfermera.
- **Plan** (`Domain/Nnn/PlanCuidados.cs`, `Application/Nnn`):
  - Tablas `plan_diagnostico`, `plan_intervencion`, `plan_intervencion_ejecucion`, `plan_resultado`, `plan_resultado_valoracion` y `plan_sugerencia`. Esta última registra cada sugerencia emitida (pendiente, confirmada, descartada o expirada) para los indicadores.
  - Un diagnóstico no vuelve a Activo. Descartar exige motivo (5 a 300) y suprime la sugerencia hasta el fin de la guardia.
  - Una valoración no se edita. Con el código del resultado se valora el resultado completo (`indicador_codigo` null).
  - No hay DELETE.
- **Integración:**
  - `SugerenciasNnnAlEvaluar` recalcula solo con evaluaciones **guardadas** (`RiesgoEvaluado.Guardada`). `SugerenciasNnnAlRegistrarLectura` recalcula con cada caída. Ambos se registran después de los manejadores de alertas.
  - `AtenderAlerta` acepta `IntervencionesEjecutadas` (`EjecucionesPlan`). `RegistrarEgreso` resuelve los activos con "Egreso" (`CierrePlanCuidados`) en la misma transacción.
  - El resumen (`ResumenPlanCuidados`) y la entrega de turno (`PlanEnEntregaTurno`) traen el plan.
- **Roles:**
  - La enfermera confirma, descarta, resuelve y valora.
  - El médico lee y solo agrega o registra la NIC 7710 (`ContextoPlanCuidados.ValidarRolAsync`, 403 `nnn-solo-enfermeria`).
  - El administrador gestiona el catálogo y ve `GET /sistema/indicadores-nnn` (solo agregados).
- **Datos clínicos:** el código y la etiqueta de un diagnóstico **nunca** van a logs, auditoría, MQTT, push ni SignalR.
  - Auditoría: `ConfirmarDiagnosticoEnfermeria`, `DescartarSugerenciaNnn`, `CerrarDiagnosticoEnfermeria`, `RegistrarIntervencion`, `ValorarResultado`, `AgregarIntervencionPlan` y `VerificarCatalogoNnn`, solo con ids.
  - Eventos de SignalR: `planActualizado` (ids) y `sugerenciasNnn` (id y cantidad).
  - Leer el plan o las sugerencias cuenta como `ConsultaHistorialPaciente`.
- **OpenAPI:** `EjemplosOpenApiNnn` agrega ejemplos de cuerpo y respuesta a cada endpoint nuevo; una prueba lo verifica.

## Pruebas de integración

- `tests/Vitalify.Api.IntegrationTests` usa `WebApplicationFactory` y un PostgreSQL 17 en Testcontainers. `FabricaVitalify` pasa la configuración como variables de entorno y aplica las migraciones antes de arrancar la API.
- **Contenedores:**
  - Casi todas las clases comparten un contenedor (colección `api`). Usan correos, códigos y DNI únicos (`ClienteApi.NuevoDni()`) y no dependen del orden.
  - Las pruebas que necesitan partir de una base vacía usan la colección `api-aislada` (`FabricaVitalifyAislada`), que tiene su propio contenedor.
  - La colección `api-mqtt` (`FabricaVitalifyMqtt`) levanta además un Mosquitto real (`eclipse-mosquitto:2`, con anónimos permitidos). `PublicarAsync` publica como el ESP32 y `EsperarAsync` espera la ingesta asíncrona.
  - La colección `api-mqtt-tls` (`FabricaVitalifyMqttTls`) es lo mismo con TLS en 8883 y un certificado autofirmado generado en la prueba: es la única con `Mqtt__ValidarCertificado=false`.
  - La colección `api-retencion` (`FabricaVitalifyRetencion`) tiene su propio contenedor: la retención resume todos los episodios que cumplan la regla, también los de otras pruebas.
  - Las colecciones no corren en paralelo (`Infraestructura/Ensamblado.cs`), porque la configuración va por variables de entorno del proceso. Una fábrica derivada agrega contenedores y variables con `ConfiguracionAdicionalAsync`.
- **Fase 5:**
  - `Fabrica.Comandos` envuelve al publicador real y registra cada comando (tópico, JSON y si va retenido). Con MQTT, además lo publica en el broker.
  - `Fabrica.SimularEstadoAsync(codigo, "online")` hace lo que el receptor con `device/{codigo}/estado`.
  - `Fabrica.RenovarCodigosAsync()` corre el worker una vez; en las fábricas está apagado con `Vinculacion__SegundosRenovacion=0`.
  - En `FabricaVitalifyMqtt`, `SuscribirAsync(topico)` se suscribe como el wearable.
  - `escaneo` admite 10 por minuto por usuario: en una misma prueba, usa otra enfermera si escaneas más.
- **Fase 6:**
  - `ClienteApi.ServicioDePruebasAsync()` (servicio `PRUEBAS`, compartido), `CrearServicioAsync`, `RegistrarUsuarioAsync(rol, servicioId?)` y `CrearCamaAsync(servicioId?)`.
  - `PruebaApi.ClienteConRolAsync(rol, servicioId?)`.
  - `Fabrica.Push` registra cada push (`DeAlerta(id)`); los tokens de `Fabrica.Push.NoRegistrados` responden "no registrado".
  - `Fabrica.VigilarAlertasAsync()` corre el vigilante una vez; en las fábricas está apagado con `Alertas__SegundosVigilancia=0`.
  - Para el escalamiento y la falta de señal, adelanta el reloj **después** de iniciar sesión: el JWT se valida con el reloj real.
- **Fase 7, parte B:** `Fabrica.ResumirHistorialAsync()` corre la retención una vez (en las fábricas está apagada con `Historial__RetencionHabilitada=false`); para que un episodio cumpla los 30 días, adelanta el reloj (`Reloj.Adelantar(TimeSpan.FromDays(31))`). Las fábricas ponen `Proxy__RedesConocidas=10.99.0.0/16` (`FabricaVitalify.RedProxy`, `IpProxy`): `CrearCliente(ip)` con una IP de esa red y `X-Forwarded-For` simula llegar por el proxy.
- **Fase 8:** `Fabrica.TiempoReal` registra cada evento de SignalR y `Fabrica.Logs` cada línea del log (para comprobar que no llevan datos clínicos). `Fabrica.SembrarCatalogoNnnAsync()` vuelve a correr la semilla del catálogo.
- **Fase 7:** `PacienteConSensorAsync` ingresa **y** vincula 6 h antes (el historial empieza en el ingreso). Para que una lectura evalúe el riesgo como si llegara en tiempo real, envíala con el reloj adelantado a su hora (`Fase7HistorialTests.EnviarHaceAsync`): el motor usa la hora de la API para la vigencia.
- **Atajos** en `ClienteApi`: `CrearCamaAsync`, `CrearDispositivoAsync`, `IngresarPacienteAsync`, `VincularAsync`, `EgresarAsync`, `RegistrarTelemetriaAsync` (entra por `/dev/telemetria`), `Ts` y `Ms`. En `PruebaApi`: `ClienteAdministradorAsync`, `ClienteEnfermeraAsync`, `ClienteMedicoAsync` y `PacienteConSensorAsync`.
- **Fábrica:**
  - `FabricaVitalify.ConsultarHistorialAsync` lee `HistorialDbContext`.
  - `SolicitudesNuevaLectura` registra las solicitudes de nueva lectura.
  - `FabricaVitalifyProduccion` (colección `api-produccion`) arranca en Production, para probar lo que solo existe en Development. Sus clientes entran como en el servidor (por el proxy conocido, con `X-Forwarded-Proto: https`); si no, la API los redirige a HTTPS. Arranca con `Simulador__Habilitado=true` para comprobar que se ignora.
- **`tests/Vitalify.Api.Tests`:** pruebas unitarias de los adaptadores de entrada (determinismo del simulador; el worker solo llama al puerto), de `OpcionesMqtt`, del proxy y CORS (`InternalsVisibleTo`) y de `CadenaDeConexion`.
- `RelojAjustable` reemplaza a `IReloj`: `using (Fabrica.Reloj.Adelantar(...))` y se restablece al salir del bloque.
- Cada `CrearCliente()` usa una IP distinta (encabezado `X-Ip-Prueba`), para que el límite de login no se comparta entre pruebas.
- Hay una prueba por escenario, con el nombre `HUxx_Ey_...`.

## Convenciones de la API

- Rutas versionadas: `api/v1/...`. Los controladores van en `Controllers/V1`.
- Errores: `ManejadorGlobalDeExcepciones` (`IExceptionHandler`) y `AddProblemDetails` con `traceId`. Todo error sale como ProblemDetails y el detalle y el stack trace solo se muestran en Development.
- `/health` usa los checks `transaccional` e `historial` (AspNetCore.HealthChecks.NpgSql, registrados en `AddInfrastructure`) y responde un JSON propio (`RespuestaHealth`).
- Swagger solo en Development (o con `Swagger__Habilitado=true`), con comentarios XML y el botón Authorize. Cada endpoint lleva `[ProducesResponseType]` y comentarios XML.
- No poner `[Produces("application/json")]` en los controladores: sobrescribe el `application/problem+json` de los errores.
- Logs con `ILogger` en JSON por consola. Los orígenes de CORS se configuran en `Cors:Origenes` (appsettings.Development o `Cors__Origenes` en el `.env`; nunca `*`).
- La configuración de la API está en `src/Vitalify.Api/Configuracion/`.
