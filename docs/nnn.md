# Proceso de enfermería con la taxonomía NNN

Vitalify **sugiere** diagnósticos de enfermería; **la enfermera decide**. Esto es apoyo a la decisión, no un diagnóstico automático: nada entra al plan de cuidados sin que una enfermera lo confirme.

NNN reúne tres taxonomías:

| Taxonomía | Qué nombra | Código | Ejemplo |
|---|---|---|---|
| **NANDA-I** | Diagnósticos de enfermería | 5 dígitos | `00032` Patrón respiratorio ineficaz |
| **NIC** | Intervenciones | 4 dígitos | `3350` Monitorización respiratoria |
| **NOC** | Resultados, valorados de 1 a 5 | 4 dígitos (indicadores: 6) | `0415` Estado respiratorio |

## Base normativa

- **Ley N° 27669, Ley del Trabajo de la Enfermera(o)** (15 de febrero de 2002), **artículo 7, inciso a)**: corresponde a la enfermera(o) "Brindar cuidado integral de enfermería basado en el Proceso de Atención de Enfermería (PAE)".
- **Reglamento de la Ley 27669, aprobado por el D.S. N° 004-2002-SA, artículo 9, inciso a)**: precisa que el PAE "incluye la valoración, el diagnóstico, la planificación, la ejecución y la evaluación del mismo, el cual será registrado obligatoriamente en la historia clínica del paciente y/o en la ficha familiar".
  - El D.S. N° 001-2026-SA modificó ese reglamento, pero solo incorporó el literal k) del artículo 11 y el artículo 11-A (consultorios de enfermería). El artículo 9 no cambió.
- **NTS N° 139-MINSA/2018/DGAIN** (gestión de la historia clínica): el registro clínico no se borra. Por eso esta fase no tiene ningún DELETE:
  - un diagnóstico se resuelve o se descarta con motivo;
  - una valoración mal hecha se corrige con otra nueva.

Los cinco pasos del PAE en Vitalify:

| Paso del PAE | Qué hace Vitalify |
|---|---|
| Valoración | Wearable, toma manual, NEWS2 y MEWS (fases 4 y 5) |
| Diagnóstico | El motor sugiere diagnósticos NANDA-I y la enfermera confirma o descarta |
| Planificación | Intervenciones NIC con su frecuencia y resultados NOC con puntuación basal y meta |
| Ejecución | Cada intervención ejecutada queda registrada, también al atender una alerta |
| Evaluación | Valoraciones NOC de 1 a 5 por indicador, con una propuesta a partir de los signos |

## Licencia del catálogo

NANDA-I (Thieme) y NIC/NOC (Elsevier) tienen derechos de autor, y este repositorio es público. Por eso:

- se siembra solo el **código** y una **etiqueta corta** en español, de un **subconjunto pequeño** (14 diagnósticos, 16 intervenciones, 11 resultados y 5 indicadores);
- **nunca** se copian definiciones, características definitorias, factores relacionados, actividades ni textos de los libros;
- cada entrada guarda la **edición** de la que viene: `NANDA-I 2024-2026`, `NIC 8a ed.` o `NOC 7a ed.`;
- el catálogo completo requiere una **licencia del editor** (Thieme para NANDA-I; Elsevier para NIC y NOC). Para usarlo en un hospital, la institución debe obtenerla y cargarlo con el mismo formato de semilla.

Los dominios y clases NANDA-I y los campos NIC de la semilla siguen la estructura que conocemos de las ediciones anteriores. La asesora debe confirmarlos contra las ediciones citadas.

## Verificación de cada entrada

La semilla es **tentativa**. Todas sus entradas quedan con `verificado = false` hasta que una **enfermera asesora** coteja el código y la etiqueta contra su libro:

1. La asesora revisa la entrada en la edición indicada. Por ejemplo, `00303 Riesgo de caídas del adulto`, que en ediciones anteriores era `00155 Riesgo de caídas`.
2. Si la entrada está bien, el administrador la marca con `PUT /api/v1/nnn/catalogo/{tipo}/{codigo}/verificacion`. `tipo` es `nanda`, `nic`, `noc` o `indicador`; queda auditado como `VerificarCatalogoNnn`.
3. Si está mal, se corrige en el JSON de la semilla, se sube la versión del archivo y se vuelve a desplegar.

El indicador de SpO2 (`080208`, en `0802 Signos vitales`) es el más dudoso. Si la asesora no lo encuentra en `0802`, se usa el de `0402 Estado respiratorio: intercambio gaseoso`.

La app muestra `verificado` junto a cada entrada, para que nadie confunda una etiqueta tentativa con la del libro.

## Semilla

- **Archivo:** `src/Vitalify.Infrastructure/Semillas/nnn-catalogo.2026.11.1.json`, un recurso embebido versionado en el nombre y en su campo `version`.
- **Cuándo se carga:** `SemillaCatalogoNnn` lo carga al arrancar, en todos los entornos, porque el catálogo hace falta también en producción.
- **Idempotente:**
  - agrega lo que falta;
  - corrige la etiqueta o la edición de las entradas **sin verificar**;
  - **nunca toca una entrada verificada**;
  - nunca borra: una entrada que sale de la semilla se queda como está.

## Motor de sugerencias (versión de reglas `2026.11.1`)

`MotorSugerenciasNnn` (`src/Vitalify.Domain/Nnn`) es C# puro: no lee la base ni la hora, y una prueba de arquitectura lo verifica. Mira cuatro cosas:

- la última `EvaluacionRiesgo`, con los puntos NEWS2 de cada parámetro;
- las alertas abiertas;
- la última toma manual **que todavía cuenta**, de donde salen el dolor y la preocupación clínica (el ACVPU y el oxígeno vienen en la evaluación);
- los diagnósticos ya activos en el plan, para no repetirlos.

Los umbrales son los **puntos NEWS2 (RCP 2017)** que ya calcula `CalculadoraNews2`, no números nuevos. Los valores que aparecen en R3, R4, R6 y R10 son los límites de sus bandas NEWS2. Las reglas son criterio clínico versionado: viven en código, no en `appsettings`.

| Regla | Se cumple si… | Diagnóstico NANDA-I | NIC | NOC | Fundamento |
|---|---|---|---|---|---|
| R1 | FR con 2 o más puntos NEWS2 (≤ 8 o ≥ 21 rpm) | 00032 Patrón respiratorio ineficaz | 3350, 3140 | 0415 | NEWS2: banda FR |
| R2 | SpO2 con 2 o más puntos (según la escala del paciente) **o** con oxígeno suplementario | 00030 Deterioro del intercambio de gases | 3320, 3350 | 0402 | NEWS2: bandas SpO2, escalas 1 y 2 |
| R3 | Temperatura ≥ 38,1 °C | 00007 Hipertermia | 3740, 6680 | 0800 | NEWS2: banda de temperatura |
| R4 | Temperatura ≤ 35,0 °C | 00006 Hipotermia | 3800, 6680 | 0800 | NEWS2: 3 puntos |
| R5 | PAS o FC con 2 o más puntos: **00029** si puntúan las dos, si no **00240** | 00029 / 00240 | 4150, 6680 | 0400, 0802 | NEWS2: bandas PAS y FC |
| R6 | PAS ≤ 90 **y** FC ≥ 131 | 00028 Riesgo de déficit de volumen de líquidos | 4250, 4120 | 0601 | NEWS2: 3 puntos en las dos |
| R7 | ACVPU distinto de A: **00128** si es C (confusión nueva), si no **00201** | 00128 / 00201 | 2620, 6490 | 0912 | NEWS2: 3 puntos |
| R8 | Alerta de caída abierta **o** ACVPU distinto de A | 00303 Riesgo de caídas del adulto | 6490 | 1909 | Caída del wearable (MPU6050) |
| R9 | Dolor ≥ 4 en la pregunta de contexto de la toma manual | 00132 Dolor agudo | 1410 | 2102 | Escala numérica 0–10 |
| R10 | Temperatura ≥ 38,1 °C, FC ≥ 91 **y** FR ≥ 21 | 00004 Riesgo de infección | 6540, 7710 | 0703 | Bandas NEWS2; el motivo dice "posible respuesta inflamatoria: avise al médico", **nunca "sepsis"** |
| R11 | NEWS2 ≥ 5 **o** algún parámetro con 3 puntos | — | agrega **7710** a todas | — | NEWS2: respuesta urgente, avisar al médico |

- **Prioridad:**
  - **alta:** 3 puntos, R4, R6, R7, R10 o una caída;
  - **media:** 2 puntos, solo oxígeno, ACVPU alterado sin caída, fiebre ≥ 39,1 °C o dolor ≥ 7;
  - **baja:** lo demás.
- **Unión:** si dos reglas proponen el mismo diagnóstico, salen como una sola sugerencia. Se unen sus NIC, NOC y motivos, y queda la prioridad mayor. `regla` es la primera; `reglas`, todas.
- **Alerta asociada:** cada sugerencia queda asociada a la alerta de riesgo abierta, o a la de caída en R8.

### Valoración NOC sugerida

Para los indicadores con `parametro_vital`, el motor propone la puntuación de 1 a 5 a partir de los puntos NEWS2 de ese signo:

| Puntos NEWS2 | Puntuación propuesta |
|---|---|
| 3 | 1 |
| 2 | 2 |
| 1 | 3 |
| 0 | 5 |

El **4 no se propone**: lo elige la enfermera. Es solo una propuesta; la valoración guarda las dos, `puntuacion` y `puntuacionSugerida`.

### Cómo se agrega o cambia una regla

1. Agregar la regla en `ReglasNnn` (`MotorSugerenciasNnn.cs`) y sumarla a `ReglasNnn.Todas`, con un id nuevo (`R12`…). Los ids no se reutilizan.
2. Escribir su prueba en `tests/Vitalify.Domain.Tests/Fase8/MotorSugerenciasNnnTests.cs` y, si corresponde, un escenario en `EscenariosClinicosNnnTests.cs`.
3. Subir `MotorSugerenciasNnn.VersionReglas`, por ejemplo a `2026.12.1`. Cada diagnóstico y cada sugerencia guardan la versión con la que se emitieron, así los indicadores se pueden separar por versión.
4. Si la regla usa un código que no está en el catálogo, agregarlo a la semilla (ver "Semilla") con `verificado = false`.
5. Actualizar la tabla de esta sección.

## Flujo

1. **Sugerencias.**
   - **Cuándo se recalculan:** con cada evaluación **guardada** (la que cambió algo; `RiesgoEvaluado.Guardada`), con cada caída y al consultarlas. Cada cambio queda en `plan_sugerencia` (emitida, confirmada, descartada o expirada), y el servicio recibe el evento `sugerenciasNnn` con el id del paciente y la cantidad.
   - **Expiración:** una sugerencia que el motor deja de proponer, porque los signos mejoraron, expira.
2. **Confirmar.** La enfermera elige las NIC (con frecuencia: `UnaVez`, `CadaTurno`, `CadaHora` o `SegunNecesidad`) y los NOC (puntuación basal y meta, de 1 a 5, con meta ≥ basal). El diagnóstico queda `Activo`. Un mismo diagnóstico no puede estar activo dos veces en la hospitalización (índice único → 409 `diagnostico-ya-activo`).
3. **Descartar.** Exige un motivo de 5 a 300 caracteres. Queda registrado como `Descartado`, para medir la tasa de aceptación, y no se vuelve a sugerir **hasta el fin de la guardia**, para que la siguiente enfermera vuelva a valorarlo.
4. **Ejecutar.** Cada intervención ejecutada guarda quién, cuándo, la alerta (opcional) y una observación de hasta 500 caracteres. Al atender una alerta se pueden enviar `intervencionesEjecutadas` y quedan con esa alerta. Una intervención `UnaVez` se desactiva al ejecutarse.
5. **Evaluar.**
   - **Qué se valora:** cada indicador de 1 a 5, o el resultado completo si se envía el código del resultado (`indicadorCodigo = null`).
   - **No se edita:** una valoración mal hecha se corrige con otra nueva.
   - **Guardia:** la valoración guarda la guardia en que se hizo, por ejemplo `2026-11-09/GuardiaDiurna`.
6. **Cerrar.**
   - `PUT .../estado` resuelve o descarta con motivo; no se puede volver a `Activo`.
   - **Al egresar,** los diagnósticos activos pasan a `Resuelto` con el motivo "Egreso" en la misma transacción del egreso, y las sugerencias pendientes expiran.

## Endpoints

| Método y ruta | Quién | Qué hace |
|---|---|---|
| `GET /api/v1/nnn/catalogo?tipo=nanda\|nic\|noc&q=` | Administrador y personal clínico | Busca por código o etiqueta, sin tildes; hasta 50 resultados |
| `GET /api/v1/nnn/diagnosticos/{codigo}/vinculos` | Administrador y personal clínico | NIC y NOC sugeridos, con los indicadores de cada NOC |
| `PUT /api/v1/nnn/catalogo/{tipo}/{codigo}/verificacion` | Administrador | Marca la entrada como verificada |
| `GET /api/v1/pacientes/{id}/nnn/sugerencias` | Personal clínico | Sugerencias actuales, con el motivo y la versión de las reglas |
| `GET /api/v1/alertas/{id}/sugerencias-nnn` | Personal clínico | Sugerencias de la evaluación que abrió la alerta |
| `GET /api/v1/pacientes/{id}/plan-cuidados?hospitalizacionId=` | Personal clínico | Plan completo, con la serie de valoraciones para graficar |
| `POST /api/v1/pacientes/{id}/plan-cuidados/diagnosticos` | Enfermera | Confirma un diagnóstico con sus NIC y NOC |
| `POST /api/v1/pacientes/{id}/plan-cuidados/sugerencias/descartar` | Enfermera | Descarta una sugerencia con motivo |
| `PUT /api/v1/plan-cuidados/diagnosticos/{id}/estado` | Enfermera | `Resuelto` o `Descartado`, con motivo |
| `POST /api/v1/plan-cuidados/diagnosticos/{id}/intervenciones` | Enfermera; el médico solo la 7710 | Agrega una NIC a un diagnóstico activo |
| `POST /api/v1/plan-cuidados/intervenciones/{id}/ejecuciones` | Enfermera; el médico solo la 7710 | Registra una ejecución |
| `POST /api/v1/plan-cuidados/resultados/{id}/valoraciones` | Enfermera | Valora los indicadores; la respuesta trae `puntuacionSugerida` |
| `GET /api/v1/plan-cuidados/resultados/{id}/valoracion-sugerida` | Personal clínico | La propuesta del motor **antes** de valorar |
| `GET /api/v1/sistema/indicadores-nnn?desde=&hasta=` | Administrador | Indicadores agregados para la tesis |

Otros cambios:

- `POST /alertas/{id}/atender` acepta `intervencionesEjecutadas`. Sin el campo, funciona igual que antes.
- `GET /pacientes/{id}/resumen` agrega `planCuidados`: los diagnósticos activos con su última valoración NOC y la cantidad de sugerencias pendientes.
- `GET /servicios/{id}/entrega-turno` agrega `planCuidados` por paciente: los diagnósticos activos, las intervenciones `CadaTurno` sin ejecutar en la guardia y los NOC sin valorar en la guardia.

Swagger tiene ejemplos de cuerpo y de respuesta para cada endpoint nuevo.

## Datos clínicos, auditoría y tiempo real

- **El diagnóstico es un dato clínico:** su código y su etiqueta nunca van a los logs, a la auditoría, a MQTT, al push ni a SignalR. Una prueba de integración recorre todo el flujo y busca los códigos y las etiquetas NANDA-I de la semilla en los logs capturados, en todos los registros de auditoría y en todos los eventos de SignalR.
- **Auditoría:** se registran, solo con ids, `ConfirmarDiagnosticoEnfermeria`, `DescartarSugerenciaNnn`, `CerrarDiagnosticoEnfermeria`, `RegistrarIntervencion`, `ValorarResultado` y `AgregarIntervencionPlan`. Leer las sugerencias, el plan o la valoración sugerida cuenta como `ConsultaHistorialPaciente`.
- **Eventos de SignalR** (al grupo del servicio):
  - `planActualizado` con `pacienteId` y `hospitalizacionId`; la app vuelve a pedir el plan por REST;
  - `sugerenciasNnn` con `pacienteId` y `cantidad`.
  - Se nombran en camelCase, como `alertaCreada` y `riesgoActualizado`, para que los clientes usen una sola convención.
- **Observaciones:** las de las ejecuciones son texto clínico libre y nunca van a los logs.

## Indicadores para la tesis

`GET /api/v1/sistema/indicadores-nnn` devuelve solo agregados. Por defecto cubre los últimos 30 días; el rango máximo es de 366 días.

- **Sugerencias:** emitidas, confirmadas, descartadas, expiradas y pendientes, en total y por regla, con dos tasas:
  - **tasa de aceptación:** confirmadas / (confirmadas + descartadas);
  - **tasa sobre emitidas:** confirmadas / emitidas.
- **Respuesta a la alerta:** tiempo desde que se creó la alerta hasta la primera intervención NIC registrada por ella (promedio y mediana, en minutos).
- **Valoración por turno:** porcentaje de diagnósticos con al menos una valoración NOC en **cada** guardia en que estuvieron activos 6 h o más, más los turnos evaluados y los turnos con valoración.
- **Cambio NOC:** cambio medio desde la puntuación basal hasta la última valoración antes del cierre, en los resultados de los diagnósticos resueltos.

## Decisiones de diseño

- **Tabla `plan_sugerencia`** (además de las del plan): registra cada sugerencia emitida y qué pasó con ella. Sin ella no se podría medir cuántas se emitieron ni cuántas expiraron sin decisión.
- **Médico y NIC 7710:** el médico lee el plan y puede **agregar** la NIC 7710 a un diagnóstico activo y **registrar** su ejecución. No confirma, descarta ni valora.
- **Lectura del catálogo:** también puede leerlo el administrador, porque es quien lo verifica y no tiene datos de pacientes.
- **Valoración global:** valorar con el código del resultado valora el resultado completo. Así se pueden valorar los NOC de la semilla que todavía no tienen indicadores.
- **Turno de la valoración:** se guarda la guardia (`2026-11-09/GuardiaDiurna`) y no el id de la programación, que se reemplaza al recargar la planilla del mes.
- **Primera semilla:** al cargarse por primera vez, el log dice cuántas entradas se agregaron (46 entradas y 49 vínculos), nunca cuáles.

## Fuera de alcance

- El catálogo completo de NANDA-I, NIC y NOC (requiere la licencia del editor).
- Definiciones, características definitorias, factores relacionados y actividades.
- Que el sistema confirme un diagnóstico por su cuenta: siempre decide una enfermera.
