# Motor clínico: NEWS2 y MEWS con el wearable de Vitalify

El backend calcula dos escalas de alerta temprana con los signos que llegan del wearable ESP32 y con los que registra el personal clínico:

- **NEWS2:** National Early Warning Score 2, Royal College of Physicians, 2017.
- **MEWS:** Modified Early Warning Score, Subbe et al., QJM 2001.

Las tablas son funciones puras (`src/Vitalify.Domain/Clinica/`) y tienen pruebas en cada límite (`tests/Vitalify.Domain.Tests/Clinica/`).

## Qué mide el hardware y qué falta

| Parámetro | NEWS2 | MEWS | Origen en Vitalify |
|---|:---:|:---:|---|
| Frecuencia cardíaca (FC) | ✓ | ✓ | **Wearable:** MAX30102 (PPG) o AD8232 (ECG) |
| SpO2 | ✓ | — | **Wearable:** MAX30102 |
| Temperatura | ✓ | ✓ | **Wearable:** DS18B20. También por observación (termómetro clínico) |
| Frecuencia respiratoria (FR) | ✓ | ✓ | **Wearable** (llega en `fr`). A mano solo si el sensor no tiene una vigente: se cuenta durante 60 s |
| Presión sistólica (PAS) | ✓ | ✓ | **Toma manual** de la enfermera (1 a 3 lecturas; se registra el promedio) |
| Nivel de conciencia (ACVPU) | ✓ | ✓ (AVPU) | **Toma manual** (asistente en cascada; el backend calcula el nivel) |
| Oxígeno suplementario | ✓ | — | **Toma manual** (dispositivo, flujo y FiO2) |

El wearable no mide presión, conciencia ni oxígeno. Por eso el motor:

1. **Calcula con lo que hay y lo declara.** Si falta un parámetro, suma 0 por él, el resultado sale con `completo: false` y `faltantes` lo nombra. **Un puntaje parcial es una cota inferior: el riesgo real puede ser mayor.**
2. **Permite completarlo con la toma manual.** La enfermera registra lo que el sensor no mide con `POST /api/v1/pacientes/{id}/observaciones`, guiada por el [asistente](#asistente-de-toma-manual). Con el wearable enviando FC, SpO2, temperatura y FR, y una toma manual vigente, NEWS2 y MEWS salen **completos**.

## Cómo se combinan las fuentes

Por cada parámetro se toma **el valor más reciente** entre dos fuentes:

- **El wearable:** su valor vale mientras esté **vigente**, es decir, si se midió hace `Telemetria__SegundosVigencia` (90 s) o menos. Una FC de hace 5 minutos no se usa.
- **La última observación:** cada valor vale hasta su [plazo de vigencia](#vigencia-de-la-toma-manual): la próxima toma más `Clinico__MinutosToleranciaToma` (30 min), con un tope de 12 h.

Lo que no esté en ninguna de las dos queda como faltante.

**Cuándo se evalúa:**
- Con cada lectura del wearable que hace avanzar el estado del paciente, cada ~10 s. Las lecturas atrasadas del búfer offline no disparan una evaluación.
- Con cada observación registrada.

Cada evaluación queda en `vitalify_historial.evaluacion_riesgo` con los valores usados, para ver la tendencia.

## Tablas

### NEWS2

| Parámetro | 3 | 2 | 1 | 0 | 1 | 2 | 3 |
|---|---|---|---|---|---|---|---|
| FR (rpm) | ≤8 | | 9–11 | 12–20 | | 21–24 | ≥25 |
| SpO2 escala 1 (%) | ≤91 | 92–93 | 94–95 | ≥96 | | | |
| SpO2 escala 2 (%) | ≤83 | 84–85 | 86–87 | 88–92 o ≥93 con aire | 93–94 con O2 | 95–96 con O2 | ≥97 con O2 |
| Aire u oxígeno | | Oxígeno | | Aire | | | |
| PAS (mmHg) | ≤90 | 91–100 | 101–110 | 111–219 | | | ≥220 |
| FC (lpm) | ≤40 | | 41–50 | 51–90 | 91–110 | 111–130 | ≥131 |
| Conciencia | | | | Alerta | | | C, V, P o U |
| Temperatura (°C) | ≤35,0 | | 35,1–36,0 | 36,1–38,0 | 38,1–39,0 | ≥39,1 | |

| Nivel (`nivelRiesgo`) | Condición | Respuesta sugerida (RCP) |
|---|---|---|
| `bajo` | 0–4 | Monitoreo de rutina, cada 4 a 12 h |
| `bajo-medio` | 0–4 con algún parámetro en 3 | Respuesta urgente en sala, al menos cada hora |
| `medio` | 5–6 | Respuesta urgente: evaluación médica, al menos cada hora |
| `alto` | ≥7 | Respuesta de emergencia y monitoreo continuo |

La escala 2 de SpO2 es para la insuficiencia respiratoria hipercápnica (objetivo 88–92 %) y la indica el médico **por paciente**:
- Se indica con `PUT /api/v1/pacientes/{id}/escala-spo2` (`{ "escala": "Escala2", "motivo": "..." }`, solo médico).
- Se guarda en la hospitalización activa y queda auditada como `EscalaSpo2Cambiada`, sin el motivo.
- El riesgo se recalcula al instante (origen `CambioEscala`), y las lecturas siguientes del wearable también usan esa escala.

Por defecto, la escala es la 1.

### MEWS

| Parámetro | 3 | 2 | 1 | 0 | 1 | 2 | 3 |
|---|---|---|---|---|---|---|---|
| PAS (mmHg) | ≤70 | 71–80 | 81–100 | 101–199 | | ≥200 | |
| FC (lpm) | | ≤40 | 41–50 | 51–100 | 101–110 | 111–129 | ≥130 |
| FR (rpm) | | <9 | | 9–14 | 15–20 | 21–29 | ≥30 |
| Temperatura (°C) | | <35 | | 35–38,4 | | ≥38,5 | |
| AVPU | | | | A | V | P | U |

- **Niveles:** `bajo` de 0 a 2, `medio` de 3 a 4 y `alto` desde 5 (Subbe: mayor riesgo de muerte o de ingreso a UCI).
- **Confusión nueva:** la C de ACVPU no existe en AVPU y se puntúa como V (1).
- **SpO2:** MEWS no la usa.

## Limitaciones del hardware y cómo se manejan

| Limitación (README del IoT) | Efecto en el puntaje | Qué hace el backend |
|---|---|---|
| El DS18B20 mide la piel, que está por debajo de la temperatura central | Una temperatura cutánea de 34 °C suma 3 en NEWS2 y 2 en MEWS, y daría falsas alarmas | **`Clinico__AjusteTemperaturaSensor`** suma un desfase en °C a la temperatura del sensor antes de puntuar (0 por defecto). Se calibra comparando con un termómetro clínico. Además, una temperatura de termómetro registrada como observación gana si es más reciente |
| La SpO2 usa la curva genérica de Maxim | Error posible de algunos puntos | Se usa tal cual; conviene calibrar `SPO2_A/B/C` en el firmware contra un oxímetro de referencia |
| No mide presión, conciencia ni oxígeno | Puntaje parcial hasta la primera toma manual | `completo: false` y `faltantes`. La vinculación por QR exige una toma inicial con los tres |
| La FR del sensor puede faltar (sin señal) | Sin FR, el puntaje es parcial | El asistente pide contarla a mano solo si la del sensor no está vigente |
| No es un dispositivo médico certificado | — | El puntaje es un apoyo a la decisión, no un diagnóstico |

## Asistente de toma manual

La app arma el asistente con la **guía versionada** `GET /api/v1/guias/toma-manual` (recurso embebido en Application, `Clinica/Guias/toma-manual.json`; versión actual `2026.11.1`). Por sección trae:
- las preguntas, con su campo en el cuerpo del POST, su tipo, sus opciones y cuándo mostrarlas;
- las recomendaciones;
- las fuentes.

Cada observación guarda la `versionGuia` con la que se tomó.

| Sección | Qué se registra | Reglas |
|---|---|---|
| Oxígeno | `recibe`; si sí: `dispositivo` (`CanulaNasal`, `MascarillaSimple`, `Venturi`, `MascarillaReservorio`, `AltoFlujo`), `flujoLpm` (0,5–60) y `fio2` (21–100 %, solo Venturi o alto flujo); `objetivo88a92` | Si tiene objetivo 88–92 % y sigue en la escala 1: aviso "Solicite al médico revisar la escala de SpO2" |
| Conciencia (ACVPU) | `despiertoCoherente` → si sí, `confusionNueva` (A o C); si no, `respondeVoz` (V) → `respondeDolor` (P) → U | **El nivel lo calcula el dominio** (`RespuestasConciencia`). Si la app manda también un `nivel` que no coincide, gana el calculado. Con `confusionBasal` (ficha del paciente) la pregunta es "¿más confundido que de costumbre?" |
| Presión (RM 031-2015/MINSA) | Checklist (`reposo5Min`, `brazoApoyadoAlturaCorazon`, `brazaleteTallaCorrecta`, `sinCafeTabacoEsfuerzo30Min`, `tensiometroValidado`), `posicion`, `brazo`, `metodo` y de 1 a 3 `lecturas` `{pas, pad, medidaEn}` | PAS y PAD = **promedio redondeado** (0,5 hacia arriba). Si hay 2 lecturas que difieren en **más de 10 mmHg** (PAS o PAD), 400 pidiendo una tercera. El checklist no bloquea, pero si está incompleto se avisa |
| Frecuencia respiratoria | `fr` | Solo si el sensor no tiene una vigente; contar 60 s sin avisar al paciente |
| Contexto | `dolor` (0–10), `ansiedadOEsfuerzoReciente`, `antipireticoUltimas4h`, `betabloqueadores` | No suma puntos: ayuda a interpretar FC y temperatura |
| Preocupación clínica | `preocupacionClinica` | "¿Le preocupa este paciente aunque el puntaje sea bajo?" (NEWS2). Aviso "se notificará al médico" (la alerta real llega en la fase 6) |

Los campos planos de la fase 4 (`pas`, `pad`, `conciencia` como texto y `oxigenoSuplementario`) siguen aceptándose. En `vitalify_historial.observacion_enfermeria`, las columnas nuevas son todas opcionales:
- oxígeno: `dispositivo_oxigeno`, `flujo_oxigeno_lpm`, `fio2` y `objetivo_spo2_88a92`;
- presión: `posicion_pa`, `brazo_pa`, `metodo_pa`, `checklist_pa` (jsonb) y `lecturas_pa` (jsonb);
- `contexto` (jsonb), `preocupacion_clinica` y `version_guia`.

### Próxima toma manual y avisos

Cada evaluación (en `GET /riesgo` y en la respuesta del POST de observaciones) trae:

- **`recomendacion`:** la respuesta clínica de la RCP según el nivel. Se mantiene `respuestaSugerida`, con el mismo texto, por compatibilidad con el dashboard.
- **`proximaTomaManualEn`:** la última toma manual más el intervalo de NEWS2. Sin ninguna toma manual, es la hora de la evaluación: la toma está pendiente.

  | NEWS2 | Intervalo |
  |---|---|
  | 0 | 12 h |
  | 1–4 | 4 h |
  | Un parámetro en 3 (bajo-medio) o 5–6 | 1 h |
  | 7 o más | 1 h (y monitoreo continuo) |

- **`avisos`:** textos para actuar en el momento. No crean alertas (la preocupación clínica sí abre una, ver [alertas-y-turnos.md](alertas-y-turnos.md)). Se calculan al leer, con los valores usados y las tomas vigentes:
  - "PAS ≤ 90 mmHg: confirme con una nueva toma y avise al médico."
  - "PAS ≥ 220 mmHg: confirme con una nueva toma y avise al médico de inmediato."
  - "Preocupación clínica registrada: se notificará al médico."
  - "Solicite al médico revisar la escala de SpO2."
  - "Checklist de presión incompleto: el valor puede no ser fiable."

`GET /riesgo` agrega además `escalaSpo2` (la vigente) y `ultimoBrazoPa` (para sugerir el mismo brazo en la toma siguiente).

### Vigencia de la toma manual

Desde la fase 6, la vigencia de un valor manual sigue al intervalo de NEWS2 (antes era un plazo fijo de 4 h, `Clinico__HorasVigenciaObservacion`, que ya no existe):

- Al registrar una toma, su evaluación calcula `proximaTomaManualEn`. La toma vale hasta **esa próxima toma más `Clinico__MinutosToleranciaToma`** (30 min), sin pasar de **12 h** desde `observadaEn`. Se guarda en `observacion_enfermeria.vigente_hasta`, y la próxima toma de cada evaluación, en `evaluacion_riesgo.proxima_toma_manual_en`.
- **Si el paciente empeora, el plazo se acorta.** Cada evaluación posterior a la toma recalcula `proximaTomaManualEn` con el NEWS2 nuevo. Manda la más reciente: si una lectura del wearable sube el NEWS2 a 5, la toma de las 08:00 que valía hasta las 20:30 pasa a valer hasta las 09:30. Una mejora no alarga el plazo guardado.
- **Al vencer**, sus parámetros vuelven a `faltantes` y el puntaje es parcial otra vez. El vigilante de alertas abre una alerta `toma-pendiente` (prioridad baja) cuando pasan la próxima toma y la tolerancia; se resuelve sola al registrar la toma siguiente.
- Las tomas anteriores a la fase 6 no guardaron su plazo: valen 4 h.

Ejemplo: toma a las 08:00 con NEWS2 0 → próxima toma a las 20:00 → los valores cuentan hasta las 20:30. Con NEWS2 3 en la toma → próxima a las 12:00 → cuentan hasta las 12:30.

### Fuentes

| Fuente | Para qué |
|---|---|
| RM N° 031-2015/MINSA: Guía de Práctica Clínica para el Diagnóstico, Tratamiento y Control de la Enfermedad Hipertensiva (Anexo 4) | Técnica de medición de la presión y checklist |
| Guía Técnica N° 006-INS/CETS-2026 (GPC de HTA en adultos; RPE N° 000093-2026-INS/PE) | Lecturas repetidas y promedio |
| Ley N° 27669, Ley del Trabajo de la Enfermera(o) | Rol de la enfermera en la valoración y el registro |
| NTS N° 139-MINSA/2018/DGAIN (Gestión de la Historia Clínica) | Registro con autor, hora y versión de la guía |
| Ley N° 29459 (productos farmacéuticos y dispositivos médicos) | Tensiómetro validado |
| Royal College of Physicians, NEWS2 (2017) | Puntajes, ACVPU, escala de SpO2, frecuencia de la toma y preocupación clínica |

Los enlaces están en la guía (`GET /api/v1/guias/toma-manual`, sección `fuentes`).

## Endpoints

| Método y ruta | Quién | Qué hace |
|---|---|---|
| `GET /api/v1/pacientes/{id}/riesgo` | Médico o Enfermera | Última evaluación: totales, nivel, `completo`, puntos por parámetro, `faltantes`, valores usados, `recomendacion`, `proximaTomaManualEn` y `avisos`; además `escalaSpo2` y `ultimoBrazoPa` |
| `POST /api/v1/pacientes/{id}/observaciones` | Médico o Enfermera | Toma manual del asistente (`oxigeno`, `conciencia`, `presion`, `fr`, `contexto`, `preocupacionClinica`, `versionGuia`, `observadaEn`) o los campos planos de la fase 4. Recalcula el riesgo y lo devuelve |
| `GET /api/v1/guias/toma-manual` | Médico o Enfermera | Guía versionada del asistente |
| `PUT /api/v1/pacientes/{id}/escala-spo2` | Médico | Indica la escala de SpO2 del paciente y recalcula el riesgo |
| `GET /api/v1/pacientes/monitoreados` | Médico o Enfermera | Por paciente, `ultimoNews2`, `ultimoMews`, `nivelRiesgo`, `evaluacionCompleta`, `senal` y `conexionSensor` |

En la observación, `conciencia` puede ser un objeto con las respuestas del asistente o, como en la fase 4, un texto (`Alerta`, `ConfusionNueva`, `RespondeVoz`, `RespondeDolor` o `NoResponde`). `observadaEn` puede ser de hasta 4 h atrás.

Las alertas que se disparan por el nivel de riesgo, la caída, la falta de señal, la toma vencida y la preocupación clínica están en [alertas-y-turnos.md](alertas-y-turnos.md).
