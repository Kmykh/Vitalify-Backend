# Matriz de permisos (HU03)

Control de acceso basado en roles (RBAC) de Vitalify. Cada rol tiene permisos diferenciados sobre los módulos y los datos de pacientes, para proteger la información clínica.

- **Roles:** `Administrador`, `Medico` y `Enfermera` (van en el claim `role` del JWT).
- **Principio:** mínimo privilegio. El administrador gestiona cuentas e inventario, pero **no accede a datos clínicos**. El personal clínico no administra cuentas.

## Leyenda

| Símbolo | Significado |
|---|---|
| **G** | Gestiona: crea, modifica y consulta |
| **L** | Solo lectura |
| **A** | Atiende: consulta y cambia el estado (por ejemplo, reconocer una alerta) |
| — | Sin acceso: la API responde **403** y registra `AccesoDenegado` en la auditoría |

## Módulos implementados (fase 1)

| Módulo | Endpoints | Administrador | Médico | Enfermera | Política |
|---|---|:---:|:---:|:---:|---|
| Inicio de sesión y renovación | `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh` | ✓ | ✓ | ✓ | Anónimo (`[AllowAnonymous]`) |
| Sesión propia | `POST /api/v1/auth/logout`, `GET /api/v1/auth/yo` | ✓ | ✓ | ✓ | Autenticado (`FallbackPolicy`) |
| Gestión de usuarios | `POST /api/v1/usuarios`, `GET /api/v1/usuarios`, `GET /api/v1/usuarios/{id}` | **G** | — | — | `SoloAdministrador` |
| Monitoreo (endpoint temporal) | `GET /api/v1/monitoreo/resumen` | — | **L** | **L** | `PersonalClinico` |
| Estado del servicio | `GET /api/v1/ping`, `GET /health` | ✓ | ✓ | ✓ | Anónimo |

El administrador solo puede registrar cuentas con rol `Medico` o `Enfermera`. El primer administrador se crea con la semilla del `.env` y no hay forma de crear otros por la API.

## Módulos futuros (permisos previstos)

Son permisos propuestos y se confirmarán al implementar cada fase.

| Módulo | Fase | Administrador | Médico | Enfermera | Política prevista |
|---|:---:|:---:|:---:|:---:|---|
| Pacientes: datos de admisión (datos personales, cama, ingreso y alta) | 2 | **G** | L | L | `SoloAdministrador` para escribir, autenticado para leer |
| Pacientes: datos clínicos (diagnóstico, antecedentes) | 2 | — | **G** | L | `SoloMedico` para escribir, `PersonalClinico` para leer |
| Sensores: inventario de wearables (alta y baja de dispositivos) | 2 | **G** | — | L | `SoloAdministrador` |
| Sensores: asignación de un wearable a un paciente | 2 | — | L | **G** | `SoloEnfermera` para escribir, `PersonalClinico` para leer |
| Signos vitales en tiempo real | 3 y 6 | — | L | L | `PersonalClinico` |
| Alertas NEWS2/MEWS | 5 | — | **A** | **A** | `PersonalClinico` |
| Historial de signos vitales y puntajes de riesgo | 6 | — | L | L | `PersonalClinico` |
| Observaciones clínicas (notas) | 6 | — | **G** | **G** | `PersonalClinico`. Cada uno edita solo sus propias notas |
| Configuración de umbrales de alerta por paciente | 6 | — | **G** | L | `SoloMedico` para escribir, `PersonalClinico` para leer |
| Consulta de la auditoría de seguridad | — | L | — | — | `SoloAdministrador` |

## Cómo se aplica

- **Políticas** (`src/Vitalify.Api/Seguridad/Politicas.cs`): `SoloAdministrador`, `PersonalClinico` (Médico o Enfermera), `SoloMedico` y `SoloEnfermera`. Se aplican con `[Authorize(Policy = Politicas.X)]`.
- **Denegar por defecto:** la `FallbackPolicy` exige autenticación en todo endpoint que no esté marcado con `[AllowAnonymous]`.
- **Respuestas:**
  - Sin token o con un token inválido, expirado o revocado: **401**.
  - Con un token válido pero sin el rol necesario: **403**.
  - Ambas salen como ProblemDetails en español.
- **Trazabilidad:** cada 403 queda en la tabla `vitalify.auditoria` con la acción `AccesoDenegado`, el usuario, la ruta y la IP.

## Evidencia (pruebas de integración)

| Escenario | Prueba |
|---|---|
| HU03 E1: una enfermera pide un endpoint de administración y recibe 403 | `HU03_E1_EnfermeraEnEndpointExclusivoDeAdministracion_Devuelve403` |
| HU03 E2: un médico con JWT válido accede a un recurso de su rol | `HU03_E2_MedicoConJwtValidoEnUnRecursoDeSuRol_AccedeCon200` |
| El administrador no accede al monitoreo clínico | `ElAdministradorNoAccedeAlMonitoreoClinico` |
| Un token con la firma alterada (rol cambiado a Administrador) es rechazado | `UnTokenConLaFirmaAlterada_Devuelve401` |

Las pruebas están en `tests/Vitalify.Api.IntegrationTests/HU03ControlDeAccesoTests.cs`.
