# Historial del paciente y notas clínicas (fase 7)

Esta parte de la fase 7 cubre:

- la HU19: historial de signos vitales y alertas;
- la parte del backend de la HU17 y la HU18:
  - el tiempo real ya llega desde la fase 6 por SignalR;
  - ahora `GET /pacientes/monitoreados` sale ordenado de mayor a menor riesgo;
- la HU22: notas clínicas.

También agrega la consulta de la auditoría para el administrador.

## Orden por riesgo (HU18)

`GET /api/v1/pacientes/monitoreados` devuelve a los pacientes de mayor a menor riesgo:

1. Por el nivel NEWS2: `alto`, `medio`, `bajo-medio`, `bajo` y, al final, `sin-datos`. Un `bajo-medio` (un parámetro en 3) va antes que un `bajo` con más puntos, porque pide respuesta urgente en sala.
2. Por el total de NEWS2 y luego el de MEWS.
3. Por la alerta abierta más grave.
4. Por la cama.

Cuando cambia el puntaje de un paciente (HU18 E2), el dashboard recibe `riesgoActualizado` por SignalR y reordena la lista sin recargarla.

## Reglas comunes del historial

- **Episodio.** Todo se consulta sobre una hospitalización:
  - por defecto, la activa o, si el paciente ya egresó, la última;
  - con `hospitalizacionId`, otra suya. Si no es suya, responde 404 `hospitalizacion-no-encontrada`;
  - si el paciente nunca estuvo hospitalizado, responde 404 `paciente-sin-hospitalizaciones`.
- **Rango.** Sin `desde` ni `hasta`, el episodio completo: del ingreso al egreso, o hasta ahora si sigue hospitalizado.

  | Consulta | Sin `agrupar` | Con `agrupar` |
  |---|---|---|
  | Signos y riesgo | Como máximo 7 días; si se pide más, 400 | El episodio completo, aunque dure más de 7 días |
  | Observaciones | Sin límite: son pocas, una cada 1 a 12 h | — |

  - Si un episodio de más de 7 días se pide sin rango ni `agrupar`, responde 400 en el campo `agrupar`.
  - Las observaciones empiezan 12 h antes del ingreso, porque una toma se puede registrar con su hora real hasta 12 h atrás.
- **Agrupar.** Los grupos se alinean a la hora UTC; en Lima (UTC−5) también caen en hora en punto. Solo aparecen los grupos con datos.

  | Serie | Valores de `agrupar` |
  |---|---|
  | Signos | `1m`, `5m`, `15m`, `1h` |
  | Riesgo | `5m`, `15m`, `1h` (una evaluación cada ~10 s; un minuto no resume nada) |

- **Cálculo en la base.** Lo agregado se calcula con SQL, con `floor(epoch / segundos)`. EF Core 8 no traduce `date_bin`. Es PostgreSQL estándar y funciona igual en TimescaleDB.
- **Quién lo lee.** Cualquier médico o enfermera (`PersonalClinico`), aunque el paciente sea de otro servicio, porque pudo cambiar de servicio. Cada consulta queda auditada como `ConsultaHistorialPaciente`, con el id del paciente y del episodio: historial, resumen, alertas, observaciones, notas y versiones. El administrador recibe 403.

## Endpoints del historial

### `GET /api/v1/pacientes/{id}/hospitalizaciones`

Todos los episodios, del más reciente al más antiguo. Cada uno trae:

- estado (`Activa` o `Finalizada`), ingreso, egreso, motivo y observación del egreso;
- cama, servicio, diagnóstico de ingreso y escala de SpO2;
- los sensores usados, con su vinculación, su liberación y el motivo (`Manual` o `Egreso`).

### `GET /api/v1/pacientes/{id}/signos/historial?hospitalizacionId=&desde=&hasta=&agrupar=`

- **Sin `agrupar`:** `lecturas` trae cada lectura tal como se guardó (solo las variables válidas), en orden cronológico.
- **Con `agrupar`:** `lecturas` llega vacía y `grupos` trae, por intervalo:

```json
{ "inicio": "2026-11-03T14:00:00Z", "fin": "2026-11-03T14:05:00Z", "lecturas": 30,
  "fc": { "promedio": 82.4, "minimo": 76, "maximo": 91 }, "spo2": { "promedio": 96.1, "minimo": 94, "maximo": 98 },
  "temperatura": null, "fr": { "promedio": 18.0, "minimo": 16, "maximo": 21 }, "pas": null, "pad": null, "caida": false }
```

Cambio respecto de la fase 6: antes, por defecto se devolvía la última hora y como máximo 24 h, solo del episodio activo. El dashboard web envía `desde` y `hasta`, así que no le afecta.

### `GET /api/v1/pacientes/{id}/riesgo/historial?hospitalizacionId=&desde=&hasta=&agrupar=`

- **Sin `agrupar`:** `evaluaciones` trae cada evaluación con NEWS2 y MEWS: total, nivel, `completo`, puntos por parámetro, `faltantes` y los valores usados. El desglose se recalcula al leer, como en `GET /riesgo`.
- **Con `agrupar`:** `grupos` trae, por intervalo:
  - `evaluaciones` y `completas` (con NEWS2 y MEWS completos);
  - de cada puntaje: `minimo`, `maximo`, `ultimo` y `nivelMaximo` (el peor nivel del intervalo).

### `GET /api/v1/pacientes/{id}/observaciones?hospitalizacionId=&desde=&hasta=`

Tomas manuales, la más reciente primero. Cada una trae:

- presión: `pas` y `pad` usadas (el promedio), cada lectura del tensiómetro, posición, brazo, método y checklist;
- FR, temperatura, conciencia (ACVPU), oxígeno (si recibe, dispositivo, flujo, FiO2 y objetivo 88–92 %), contexto, preocupación clínica y versión de la guía;
- `registradaPor` con el nombre y el rol del profesional;
- `vigenteHasta`: hasta cuándo contaron sus valores para el puntaje.

### `GET /api/v1/pacientes/{id}/alertas?hospitalizacionId=`

Las alertas del episodio, la más reciente primero, con los tiempos de respuesta.

Cambio respecto de la fase 6: antes devolvía las alertas de todos los episodios, pero solo las del servicio de quien consultaba. Ahora sigue las reglas del historial: es por episodio, la lee cualquier servicio y queda auditada.

### `GET /api/v1/pacientes/{id}/resumen?hospitalizacionId=`

Todo lo que el médico necesita del episodio en una respuesta:

| Campo | Contenido |
|---|---|
| `paciente` | Id, nombre, documento, fecha de nacimiento, edad y confusión basal |
| `episodio` | Igual que en `hospitalizaciones` |
| `ultimoRiesgo` | La última evaluación, como en `GET /riesgo` (con recomendación y avisos) |
| `extremos.sensor` | Mínimo, máximo y cantidad de mediciones por variable, de las lecturas del wearable |
| `extremos.tomaManual` | Lo mismo, de las tomas manuales (FR, temperatura, PAS y PAD) |
| `alertas` | Las cinco categorías: cantidad, abiertas y tiempo medio en segundos hasta tomada y hasta atendida |
| `alertasAbiertas` | Las alertas pendientes o en atención |
| `ultimasNotas` | Las 5 notas más recientes |
| `proximaTomaManualEn` y `tomaVencida` | Solo en un episodio activo |

Algunas aclaraciones:

- **Por qué los extremos van separados:** la temperatura del sensor es de piel (DS18B20) y la de la toma manual es central. Juntarlas daría un mínimo engañoso.
- **El tiempo medio hasta atendida** solo cuenta las alertas que cerró una persona, no las resueltas automáticamente.

## Notas clínicas (HU22)

- **Tablas:**
  - `vitalify.nota_clinica`: `id`, `hospitalizacion_id`, `paciente_id`, `autor_id`, `tipo`, `texto`, `creada_en`, `editada_en` y `version`.
  - `vitalify.nota_clinica_version`: `nota_id`, `numero`, `tipo`, `texto`, `vigente_desde` y `reemplazada_en`.
- **Tipos:** `Evolucion`, `Enfermeria` o `Interconsulta`. El texto va de 5 a 4000 caracteres.

| Acción | Endpoint | Regla |
|---|---|---|
| Registrar | `POST /api/v1/pacientes/{id}/notas` con `{ "tipo": "Evolucion", "texto": "..." }` | Médico o enfermera, en la hospitalización activa (si no hay, 409 `sin-hospitalizacion-activa`). Queda con fecha, hora y autor (HU22 E1). Responde 201 |
| Listar | `GET /api/v1/pacientes/{id}/notas?hospitalizacionId=` | Las del episodio, la más reciente primero, con `autor` (nombre y rol), `version` y `editableHasta` |
| Corregir | `PUT /api/v1/notas/{id}` con `{ "texto": "...", "tipo": "..." }` | Solo el autor (si no, 403 `nota-de-otro-autor`) y dentro de las 24 h desde que la escribió (si no, 409 `nota-fuera-de-plazo`). El texto anterior pasa a `nota_clinica_version` y la nota guarda `editadaEn` (HU22 E2). Si nada cambia, no se crea versión |
| Versiones | `GET /api/v1/notas/{id}/versiones` | De la más reciente a la original |

- **Nada se borra:** no hay DELETE (responde 405) y una versión nunca se modifica (NTS N° 139-MINSA/2018/DGAIN).
- **Concurrencia:** dos correcciones a la vez dan 409. La nota usa `xmin` como token (`conflicto-concurrencia`) y el número de versión es único (`nota-editada-concurrente`).
- **Auditoría:** se audita `NotaRegistrada` y `NotaEditada`, con ids. El texto nunca va a la auditoría ni al log.

## Auditoría para el administrador

`GET /api/v1/auditoria?accion=&usuarioId=&desde=&hasta=&pagina=&tamano=` (`SoloAdministrador`, hasta 100 por página, el más reciente primero).

Cada registro trae solo `accion`, `usuarioId`, `fecha`, `ip` y `referencias`, es decir, los ids que menciona: paciente, hospitalización, alerta, dispositivo, etc.

**El texto del detalle no sale**, porque puede llevar dos cosas que el administrador no debe ver (Ley 29733):
- el motivo de un egreso, por ejemplo `Fallecimiento`;
- el correo de un login fallido.

## Fuera de alcance: umbrales de alerta por paciente

No se implementan umbrales de alerta personalizados por paciente. NEWS2 (RCP 2017) y MEWS (Subbe 2001) son escalas estandarizadas y validadas: cambiar sus cortes por paciente invalida el puntaje y su respuesta clínica. Lo que estas escalas sí permiten personalizar ya existe desde la fase 5: la escala de SpO2 del paciente (`PUT /pacientes/{id}/escala-spo2`, solo el médico).
