# Matriz de permisos (HU03)

Control de acceso basado en roles (RBAC) de Vitalify. Cada rol tiene permisos diferenciados sobre los módulos y los datos de pacientes, para proteger la información clínica.

- **Roles:** `Administrador`, `Medico` y `Enfermera` (van en el claim `role` del JWT).
- **Principio de mínimo privilegio (Ley 29733, Protección de Datos Personales):**
  - El administrador gestiona cuentas y catálogos (servicios, camas y dispositivos) y carga la programación de turnos, pero **no accede a datos de pacientes ni a las alertas**. En el catálogo de camas ve si una cama está ocupada, pero no quién la ocupa.
  - El personal clínico no administra cuentas ni el inventario.
  - Según el backlog (HU05, HU06 y HU08), la **enfermera** registra el ingreso, vincula el sensor y registra el egreso.

## Leyenda

| Símbolo | Significado |
|---|---|
| **G** | Gestiona: crea, modifica y consulta |
| **L** | Solo lectura |
| **A** | Atiende: consulta y cambia el estado (por ejemplo, reconocer una alerta) |
| — | Sin acceso: la API responde **403** y registra `AccesoDenegado` en la auditoría |

## Módulos implementados

| Módulo | Endpoints | Administrador | Médico | Enfermera | Política |
|---|---|:---:|:---:|:---:|---|
| Inicio de sesión y renovación | `POST /auth/login`, `POST /auth/refresh` | ✓ | ✓ | ✓ | Anónimo (`[AllowAnonymous]`) |
| Sesión propia | `POST /auth/logout`, `GET /auth/yo` | ✓ | ✓ | ✓ | Autenticado (`FallbackPolicy`) |
| Gestión de usuarios (las enfermeras y los médicos nuevos llevan `servicioId`) | `POST /usuarios`, `GET /usuarios`, `GET /usuarios/{id}`, `PUT /usuarios/{id}/servicio` | **G** | — | — | `SoloAdministrador` |
| Servicios hospitalarios (fase 6) | `POST /servicios` · `GET /servicios` | **G** | L | L | `SoloAdministrador` para escribir · `AdministradorOPersonalClinico` para leer |
| Programación mensual de turnos (planilla) | `PUT /servicios/{id}/programacion/{anio}/{mes}` · `GET` de la misma ruta | **G** | L | L | `SoloAdministrador` para cargar · `AdministradorOPersonalClinico` para leer |
| Turno vigente y turnos propios | `GET /turnos/actual?servicioId=`, `GET /turnos/mios?desde=&hasta=` | — | L | L | `PersonalClinico` |
| Camas (catálogo) | `POST /camas` · `GET /camas?soloDisponibles=` | **G** | L | L | `SoloAdministrador` para escribir · `AdministradorOPersonalClinico` para leer |
| Dispositivos (inventario y estado) | `POST /dispositivos`, `PATCH /dispositivos/{id}/estado` · `GET /dispositivos?estado=` | **G** | L | L | `SoloAdministrador` para escribir · `AdministradorOPersonalClinico` para leer |
| Pacientes: ingreso, edición de datos básicos y egreso (HU05, HU08) | `POST /pacientes`, `PUT /pacientes/{id}`, `POST /pacientes/{id}/egreso` · `GET /pacientes`, `GET /pacientes/{id}` | — | L | **G** | `SoloEnfermera` para escribir · `PersonalClinico` para leer |
| Vincular y liberar un sensor (HU06) | `POST /pacientes/{id}/dispositivo`, `DELETE /pacientes/{id}/dispositivo` | — | L | **G** | `SoloEnfermera` para escribir · `PersonalClinico` para leer (en la ficha) |
| Vinculación por QR (HU06, fase 5) | `POST /vinculaciones/escaneo` (10 por minuto por usuario), `POST /vinculaciones/confirmar` | — | — | **G** | `SoloEnfermera` |
| Regenerar el QR de un sensor | `POST /dispositivos/{id}/qr` | **G** | **G** | **G** | `AdministradorOPersonalClinico` (no devuelve el código: solo lo muestra el wearable) |
| Pacientes monitoreados (HU07), con última lectura y señal | `GET /pacientes/monitoreados` | — | L | L | `PersonalClinico` |
| Signos vitales actuales (HU10, HU11 y HU12) | `GET /pacientes/{id}/signos/actual` | — | L | L | `PersonalClinico` |
| Historial del paciente (HU19, fase 7): episodios, signos, riesgo, tomas manuales y resumen | `GET /pacientes/{id}/hospitalizaciones`, `.../signos/historial`, `.../riesgo/historial`, `.../observaciones`, `.../resumen` | — | L | L | `PersonalClinico`, de cualquier servicio; cada consulta se audita (`ConsultaHistorialPaciente`) |
| Notas clínicas (HU22, fase 7) | `POST /pacientes/{id}/notas`, `PUT /notas/{id}` · `GET /pacientes/{id}/notas`, `GET /notas/{id}/versiones` | — | **G** | **G** | `PersonalClinico`; solo el autor corrige y solo dentro de las 24 h; no hay borrado |
| Consulta de la auditoría de seguridad (fase 7) | `GET /auditoria` | L | — | — | `SoloAdministrador`; solo acciones e ids, sin el texto del detalle |
| Riesgo NEWS2 y MEWS (fase 4) | `GET /pacientes/{id}/riesgo` | — | L | L | `PersonalClinico` |
| Toma manual (asistente: presión, conciencia, oxígeno, FR, contexto, preocupación) | `POST /pacientes/{id}/observaciones` | — | **G** | **G** | `PersonalClinico` |
| Guía de toma manual | `GET /guias/toma-manual` | — | L | L | `PersonalClinico` |
| Escala de SpO2 del paciente | `PUT /pacientes/{id}/escala-spo2` | — | **G** | — | `SoloMedico` |
| Alertas clínicas de su servicio (fase 6) | `GET /alertas` · `POST /alertas/{id}/tomar`, `POST /alertas/{id}/atender` | — | **A** | **A** | `PersonalClinico`, y solo las del servicio del usuario (si no, 403 `alerta-de-otro-servicio`) |
| Alertas de un episodio del paciente (historial) | `GET /pacientes/{id}/alertas?hospitalizacionId=` | — | L | L | `PersonalClinico`, de cualquier servicio; se audita como `ConsultaHistorialPaciente` |
| Entrega de turno (reporte de enfermería) | `GET /servicios/{id}/entrega-turno` | — | L | L | `PersonalClinico`, solo de su servicio |
| Eventos en tiempo real (SignalR) | `/hubs/monitoreo` | — | L | L | `PersonalClinico` (el administrador no puede conectarse) |
| Token push del celular | `POST /notificaciones/dispositivos`, `DELETE /notificaciones/dispositivos` | — | **G** | **G** | `PersonalClinico`, cada uno solo los suyos |
| Incidencias de telemetría (datos técnicos, sin datos del paciente) | `GET /telemetria/incidencias` | L | — | — | `SoloAdministrador` |
| Ingesta de telemetría de desarrollo (solo Development) | `POST /dev/telemetria` | **G** | — | — | `SoloAdministrador` |
| Estado del servicio | `GET /ping`, `GET /health` | ✓ | ✓ | ✓ | Anónimo |

Todas las rutas empiezan por `/api/v1`, salvo `/health`.

Notas:
- El administrador solo registra cuentas con rol `Medico` o `Enfermera`. El primer administrador se crea con la semilla del `.env`.
- No existe ningún endpoint para borrar pacientes ni hospitalizaciones. El egreso archiva la hospitalización (queda `Finalizada`).
- Cada consulta a la ficha de un paciente (`GET /pacientes/{id}`) queda registrada como `ConsultaFichaPaciente`.
- **Telemetría:**
  - Los signos del paciente son información clínica, así que solo los ve el personal clínico.
  - Las incidencias de la ingesta son datos técnicos del sensor (dispositivo, variable, valor recibido, ids), así que las revisa el administrador. Nunca incluyen el nombre ni el documento del paciente.
  - La ingesta real por MQTT no usa usuarios de la API: la autentica el broker (cada wearable con su código y clave, y un ACL por tópico). El endpoint de desarrollo exige un administrador.
- **Observaciones:** las registra el médico o la enfermera y quedan auditadas como `ObservacionRegistrada`.
- **Vinculación por QR:** solo la enfermera escanea y confirma.
  - Se audita `QrGenerado`, `QrEscaneado`, `QrRechazado` (con el motivo) y `VinculacionPorQr`, solo con ids.
  - Al wearable solo van la cama y las iniciales del paciente.
  - Los comandos MQTT al wearable los autentica el broker: el backend escribe en `device/+/comandos` y cada wearable lee solo el suyo.
- **Escala de SpO2:** la indica solo el médico. Queda auditada como `EscalaSpo2Cambiada`, sin el motivo (texto libre).
- **Servicios y turnos (fase 6):**
  - Cada enfermera y cada médico pertenece a un servicio (`usuario.servicio_id`). Sin servicio, las alertas y la entrega de turno responden 403 `usuario-sin-servicio`.
  - El administrador carga la planilla, que solo contiene ids de usuarios y letras. Se audita como `ProgramacionCargada` y la asignación de servicio como `ServicioAsignado`.
- **Historial y notas (fase 7):**
  - El historial lo lee un médico o una enfermera de **cualquier** servicio, porque el paciente pudo cambiar de servicio. Cada consulta queda como `ConsultaHistorialPaciente`, con el id del paciente y del episodio.
  - Las notas las escriben el médico y la enfermera. Solo el autor las corrige, dentro de las 24 h, y el texto anterior se conserva como versión (NTS 139). Se audita `NotaRegistrada` y `NotaEditada`, sin el texto.
  - `GET /auditoria` muestra al administrador la acción, el usuario, la fecha, la IP y los ids que menciona cada registro. El detalle no sale, porque puede llevar el motivo de un egreso o el correo de un login fallido.
- **Alertas (fase 6):**
  - Una enfermera o un médico toma y atiende las alertas de **su** servicio. Si otra persona ya la tomó, tomarla da 409 `alerta-tomada-por-otro`; atenderla sí se permite a cualquiera del servicio.
  - Se audita `AlertaTomada` y `AlertaAtendida` con el id de la alerta. La nota de atención queda en la alerta, no en la auditoría.
  - Los eventos de SignalR y los push llevan el id del paciente, la cama y los puntajes; **nunca** el nombre, el DNI ni el diagnóstico. El nombre lo pide el cliente por la API, con su permiso.

### Cambio respecto de la versión anterior

La versión de la fase 1 proponía que el administrador gestionara la admisión de pacientes. Se corrigió para que coincida con el backlog: la **enfermera** registra el ingreso, vincula el sensor y da el egreso, y el administrador queda **sin acceso** a los datos de pacientes.

## Módulos previstos y fuera de alcance

Todo lo que preveía esta sección ya está implementado (tiempo real, alertas, historial de puntajes, notas clínicas y consulta de la auditoría), salvo un módulo que queda fuera de alcance:

| Módulo | Decisión | Motivo |
|---|---|---|
| Configuración de umbrales de alerta por paciente | Fuera de alcance | NEWS2 (RCP 2017) y MEWS (Subbe 2001) son escalas estandarizadas y validadas: cambiar sus cortes por paciente invalida el puntaje y su respuesta clínica. Lo personalizable ya existe: la escala de SpO2 del paciente (fase 5, `SoloMedico`) |

## Cómo se aplica

- **Políticas** (`src/Vitalify.Api/Seguridad/Politicas.cs`): `SoloAdministrador`, `PersonalClinico` (Médico o Enfermera), `SoloMedico`, `SoloEnfermera` y `AdministradorOPersonalClinico` (los tres roles; se usa para leer catálogos que no contienen datos de pacientes). Se aplican con `[Authorize(Policy = Politicas.X)]`.
- **Denegar por defecto:** la `FallbackPolicy` exige autenticación en todo endpoint que no esté marcado con `[AllowAnonymous]`.
- **Respuestas:**
  - Sin token o con un token inválido, expirado o revocado: **401**.
  - Con un token válido pero sin el rol necesario: **403**.
  - Ambas salen como ProblemDetails en español.
- **Trazabilidad:**
  - Cada 403 queda en `vitalify.auditoria` como `AccesoDenegado`, con el usuario, la ruta y la IP.
  - Las operaciones sobre pacientes se auditan con ids, **sin nombres ni números de documento**: `PacienteIngresado`, `PacienteActualizado`, `PacienteEgresado`, `DispositivoVinculado`, `DispositivoLiberado` y `ConsultaFichaPaciente`.
  - Desde la fase 6 también `ProgramacionCargada`, `ServicioAsignado`, `AlertaTomada` y `AlertaAtendida`; desde la fase 7, `ConsultaHistorialPaciente`, `NotaRegistrada` y `NotaEditada`.

## Evidencia (pruebas de integración)

| Escenario | Prueba |
|---|---|
| HU03 E1: una enfermera pide un endpoint de administración y recibe 403 | `HU03_E1_EnfermeraEnEndpointExclusivoDeAdministracion_Devuelve403` |
| HU03 E2: un médico con JWT válido accede a un recurso de su rol (`GET /pacientes/monitoreados`) | `HU03_E2_MedicoConJwtValidoEnUnRecursoDeSuRol_AccedeCon200` |
| El administrador no accede al monitoreo clínico | `ElAdministradorNoAccedeAlMonitoreoClinico` |
| El administrador recibe 403 en monitoreados, en el ingreso y en el listado de pacientes | `ElAdministradorRecibe403EnMonitoreadosYEnElIngresoDePacientes` |
| La enfermera recibe 403 al registrar dispositivos | `LaEnfermeraRecibe403AlRegistrarDispositivos` |
| La enfermera recibe 403 en las incidencias técnicas y el administrador recibe 403 en los signos del paciente | `LaEnfermeraRecibe403EnIncidenciasYElAdministrador403EnLosSignos` |
| El administrador no ve el riesgo ni registra observaciones; el médico sí | `ElAdministradorNoVeElRiesgoNiRegistraObservacionesYElMedicoSi` |
| El administrador recibe 403 en el historial de signos | `ElAdministradorRecibe403YUnPacienteInexistente404` |
| El médico recibe 403 al ingresar pacientes | `ElMedicoRecibe403AlIngresarPacientes` |
| Solo la enfermera escanea y confirma la vinculación por QR | `SoloLaEnfermeraEscaneaYConfirma` |
| Administrador y personal clínico regeneran el QR; no si el sensor está vinculado | `RegenerarQr_LoPuedenPedirAdministradorYPersonalClinico_PeroNoSiElSensorEstaVinculado` |
| Solo el médico cambia la escala de SpO2 y la auditoría no guarda el motivo | `ElMedicoCambiaLaEscalaDeSpo2_SeAuditaYElRiesgoSeRecalculaAlInstante` |
| La auditoría de la vinculación por QR no contiene el nombre ni el documento | `QR_E5_ConfirmarConPacienteNuevoCamaLibreYObservacionInicial_PacienteHospitalizadoVinculadoYPrimerRiesgo` |
| El administrador ve la ocupación de las camas sin datos del paciente | `ElAdministradorVeLaOcupacionDeLaCamaPeroNoQuienLaOcupa` |
| La auditoría del ingreso no contiene el nombre ni el documento | `HU05_E1_EnfermeraCompletaNombreEdadCamaYDiagnostico_PacienteRegistradoYHabilitadoParaSensor` |
| Un token con la firma alterada (rol cambiado a Administrador) es rechazado | `UnTokenConLaFirmaAlterada_Devuelve401` |
| El administrador no ve alertas, la entrega de turno ni el turno vigente, pero sí la programación | `ElAdministradorNoVeAlertasNiLaEntregaDeTurno_PeroSiLaProgramacion` |
| Solo el administrador crea servicios y asigna el servicio de un usuario | `ElAdministradorAsignaElServicioDeUnaEnfermeraYCreaServicios` |
| Una enfermera de otro servicio recibe 403 al ver o tomar una alerta | `AL_E8_UnaEnfermeraDeOtroServicioRecibe403` |
| Atender exige una nota y queda auditado | `AL_E7_AtenderSinNotaDa400_YConNotaQuedaAtendidaYAuditada` |
| Sin token o con el del administrador no se puede conectar al hub | `SignalR_SinTokenOConElTokenDelAdministradorNoSePuedeConectar` |
| Un médico de otro servicio lee el historial y queda auditado | `HU19_E1_UnMedicoDeOtroServicioVeLaLineaDeTiempoDeSignosRiesgoYAlertas_YQuedaAuditado` |
| El administrador no ve el historial ni las notas | `ElAdministradorNoVeElHistorialNiLasNotasDeUnPaciente` |
| Solo el autor corrige una nota, dentro de 24 h, y no se borra | `HU22_E2_ElAutorLaCorrigeDentroDe24h_SeConservaLaVersionAnterior_YNadieLaBorra` |
| La auditoría muestra al administrador solo acciones e ids, sin el motivo del egreso | `LaAuditoriaMuestraAlAdministradorSoloAccionesEIds_SinElMotivoDelEgreso` |

Las pruebas están en `tests/Vitalify.Api.IntegrationTests/` (`HU03ControlDeAccesoTests.cs`, `PermisosFase2Tests.cs`, `HU05RegistroDePacienteTests.cs`, `TelemetriaExtrasTests.cs`, `HU06VinculacionPorQrTests.cs`, `TomaManualTests.cs`, `Fase6AlertasTests.cs`, `Fase6TurnosTests.cs` y `Fase7HistorialTests.cs`).
