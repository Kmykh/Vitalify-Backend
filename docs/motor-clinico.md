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
| Frecuencia respiratoria (FR) | ✓ | ✓ | **Observación** del personal clínico |
| Presión sistólica (PAS) | ✓ | ✓ | **Observación** (se registra con la diastólica) |
| Nivel de conciencia (ACVPU) | ✓ | ✓ (AVPU) | **Observación** |
| Oxígeno suplementario | ✓ | — | **Observación** |

El wearable no mide FR, presión, conciencia ni oxígeno. Por eso el motor:

1. **Calcula con lo que hay y lo declara.** Si falta un parámetro, suma 0 por él, el resultado sale con `completo: false` y `faltantes` lo nombra. **Un puntaje parcial es una cota inferior: el riesgo real puede ser mayor.**
2. **Permite completarlo con observaciones.** El personal clínico registra lo que el sensor no mide con `POST /api/v1/pacientes/{id}/observaciones`, como en la toma manual de signos de la práctica habitual.

## Cómo se combinan las fuentes

Por cada parámetro se toma **el valor más reciente** entre dos fuentes:

- **El wearable:** su valor vale mientras esté **vigente**, es decir, si se midió hace `Telemetria__SegundosVigencia` (90 s) o menos. Una FC de hace 5 minutos no se usa.
- **La última observación:** cada valor vale durante `Clinico__HorasVigenciaObservacion` (4 h).

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

La escala 2 de SpO2 es para la insuficiencia respiratoria hipercápnica (objetivo 88–92 %) y la indica el médico. Hoy todas las evaluaciones usan la escala 1. Indicar la escala 2 por paciente queda para la configuración de umbrales (fase 6).

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
| No mide FR, presión, conciencia ni oxígeno | Puntaje parcial | `completo: false` y `faltantes`, que se completan con observaciones |
| No es un dispositivo médico certificado | — | El puntaje es un apoyo a la decisión, no un diagnóstico |

## Endpoints

| Método y ruta | Quién | Qué hace |
|---|---|---|
| `GET /api/v1/pacientes/{id}/riesgo` | Médico o Enfermera | Última evaluación: totales, nivel, `completo`, puntos por parámetro, `faltantes`, valores usados y la respuesta sugerida |
| `POST /api/v1/pacientes/{id}/observaciones` | Médico o Enfermera | `{ fr, pas, pad, temperatura, conciencia, oxigenoSuplementario, observadaEn }` (todos opcionales, al menos uno). Recalcula el riesgo y lo devuelve |
| `GET /api/v1/pacientes/monitoreados` | Médico o Enfermera | Por paciente, `ultimoNews2`, `ultimoMews`, `nivelRiesgo`, `evaluacionCompleta`, `senal` y `conexionSensor` |

En la observación, `conciencia` puede ser `Alerta`, `ConfusionNueva`, `RespondeVoz`, `RespondeDolor` o `NoResponde`. La presión va con `pas` y `pad` juntas, y la sistólica debe ser mayor. `observadaEn` puede ser de hasta 4 h atrás.

Las alertas que se disparan por un nivel `medio` o `alto` llegan en la fase 5.
