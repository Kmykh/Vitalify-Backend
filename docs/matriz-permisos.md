# Matriz de permisos (HU03)

Control de acceso basado en roles (RBAC) de Vitalify. Cada rol tiene permisos diferenciados sobre los módulos y los datos de pacientes, para proteger la información clínica.

- **Roles:** `Administrador`, `Medico` y `Enfermera` (van en el claim `role` del JWT).
- **Principio de mínimo privilegio (Ley 29733, Protección de Datos Personales):**
  - El administrador gestiona cuentas y catálogos (camas y dispositivos), pero **no accede a datos de pacientes**. En el catálogo de camas ve si una cama está ocupada, pero no quién la ocupa.
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
| Gestión de usuarios | `POST /usuarios`, `GET /usuarios`, `GET /usuarios/{id}` | **G** | — | — | `SoloAdministrador` |
| Camas (catálogo) | `POST /camas` · `GET /camas?soloDisponibles=` | **G** | L | L | `SoloAdministrador` para escribir · `AdministradorOPersonalClinico` para leer |
| Dispositivos (inventario y estado) | `POST /dispositivos`, `PATCH /dispositivos/{id}/estado` · `GET /dispositivos?estado=` | **G** | L | L | `SoloAdministrador` para escribir · `AdministradorOPersonalClinico` para leer |
| Pacientes: ingreso, edición de datos básicos y egreso (HU05, HU08) | `POST /pacientes`, `PUT /pacientes/{id}`, `POST /pacientes/{id}/egreso` · `GET /pacientes`, `GET /pacientes/{id}` | — | L | **G** | `SoloEnfermera` para escribir · `PersonalClinico` para leer |
| Vincular y liberar un sensor (HU06) | `POST /pacientes/{id}/dispositivo`, `DELETE /pacientes/{id}/dispositivo` | — | L | **G** | `SoloEnfermera` para escribir · `PersonalClinico` para leer (en la ficha) |
| Pacientes monitoreados (HU07) | `GET /pacientes/monitoreados` | — | L | L | `PersonalClinico` |
| Estado del servicio | `GET /ping`, `GET /health` | ✓ | ✓ | ✓ | Anónimo |

Todas las rutas empiezan por `/api/v1`, salvo `/health`.

Notas:
- El administrador solo registra cuentas con rol `Medico` o `Enfermera`. El primer administrador se crea con la semilla del `.env`.
- No existe ningún endpoint para borrar pacientes ni hospitalizaciones. El egreso archiva la hospitalización (queda `Finalizada`).
- Cada consulta a la ficha de un paciente (`GET /pacientes/{id}`) queda registrada como `ConsultaFichaPaciente`.

### Cambio respecto de la versión anterior

La versión de la fase 1 proponía que el administrador gestionara la admisión de pacientes. Se corrigió para que coincida con el backlog: la **enfermera** registra el ingreso, vincula el sensor y da el egreso, y el administrador queda **sin acceso** a los datos de pacientes.

## Módulos futuros (permisos previstos)

Se confirmarán al implementar cada fase.

| Módulo | Fase | Administrador | Médico | Enfermera | Política prevista |
|---|:---:|:---:|:---:|:---:|---|
| Signos vitales en tiempo real | 3 y 6 | — | L | L | `PersonalClinico` |
| Puntajes NEWS2/MEWS en la lista de monitoreo | 4 | — | L | L | `PersonalClinico` |
| Alertas NEWS2/MEWS | 5 | — | **A** | **A** | `PersonalClinico` |
| Historial de signos vitales y puntajes de riesgo | 6 | — | L | L | `PersonalClinico` |
| Observaciones clínicas (notas) | 6 | — | **G** | **G** | `PersonalClinico`. Cada uno edita solo sus propias notas |
| Configuración de umbrales de alerta por paciente | 6 | — | **G** | L | `SoloMedico` para escribir, `PersonalClinico` para leer |
| Consulta de la auditoría de seguridad | — | L | — | — | `SoloAdministrador` |

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

## Evidencia (pruebas de integración)

| Escenario | Prueba |
|---|---|
| HU03 E1: una enfermera pide un endpoint de administración y recibe 403 | `HU03_E1_EnfermeraEnEndpointExclusivoDeAdministracion_Devuelve403` |
| HU03 E2: un médico con JWT válido accede a un recurso de su rol (`GET /pacientes/monitoreados`) | `HU03_E2_MedicoConJwtValidoEnUnRecursoDeSuRol_AccedeCon200` |
| El administrador no accede al monitoreo clínico | `ElAdministradorNoAccedeAlMonitoreoClinico` |
| El administrador recibe 403 en monitoreados, en el ingreso y en el listado de pacientes | `ElAdministradorRecibe403EnMonitoreadosYEnElIngresoDePacientes` |
| La enfermera recibe 403 al registrar dispositivos | `LaEnfermeraRecibe403AlRegistrarDispositivos` |
| El médico recibe 403 al ingresar pacientes | `ElMedicoRecibe403AlIngresarPacientes` |
| El administrador ve la ocupación de las camas sin datos del paciente | `ElAdministradorVeLaOcupacionDeLaCamaPeroNoQuienLaOcupa` |
| La auditoría del ingreso no contiene el nombre ni el documento | `HU05_E1_EnfermeraCompletaNombreEdadCamaYDiagnostico_PacienteRegistradoYHabilitadoParaSensor` |
| Un token con la firma alterada (rol cambiado a Administrador) es rechazado | `UnTokenConLaFirmaAlterada_Devuelve401` |

Las pruebas están en `tests/Vitalify.Api.IntegrationTests/` (`HU03ControlDeAccesoTests.cs`, `PermisosFase2Tests.cs` y `HU05RegistroDePacienteTests.cs`).
