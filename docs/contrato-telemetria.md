# Contrato de telemetría (firmware ESP32 → backend)

Este documento define el mensaje que publica el wearable ESP32 con los signos vitales del paciente (firmware en `IOT/tesis_V01`, repositorio `Kmykh/IoT-Vitalfy`). Lo usan todos los adaptadores de entrada: el **receptor MQTT**, que es el camino real, y, solo en Development, el endpoint de pruebas y el simulador.

El backend lo interpreta en un solo lugar (`MensajeTelemetria`) y lo pasa al único puerto de entrada de la telemetría, `IRegistrarLectura`. El núcleo no sabe quién envió la lectura: solo guarda su `origen` (`Simulador`, `ApiDesarrollo` o `Mqtt`) para trazabilidad.

## Transporte: MQTT

```
ESP32 (usuario = código)  ──QoS 1──>  Mosquitto (IOT/tesis_V01/broker)  ──QoS 1──>  ReceptorMqtt (backend)
   device/ESP32-001/telemetria                                                       → MensajeTelemetria → IRegistrarLectura
   device/ESP32-001/estado  (online / offline, retenido)                             → presencia del wearable
```

| Aspecto | Valor |
|---|---|
| Tópico de lecturas | `device/{codigo}/telemetria`. `{codigo}` es el código del dispositivo registrado en Vitalify (patrón `^[A-Z0-9-]{3,32}$`, por ejemplo `ESP32-001`). Es también el usuario MQTT del wearable |
| Tópico de estado | `device/{codigo}/estado`: `online` al conectar y `offline` como *last will* (lo publica el broker si el ESP32 desaparece). Ambos son retenidos |
| QoS | **1** (al menos una vez), en el wearable y en el backend |
| Formato | JSON en UTF-8 |
| Frecuencia | Una lectura cada 10 s, y otra al instante si se confirma una caída |
| Búfer del wearable | Hasta 360 lecturas (1 h) sin red ni broker; se envían en orden al volver |

**Backend (`src/Vitalify.Api/Entrada/Mqtt/ReceptorMqtt.cs`):**
- **Conexión:** entra como `vitalify-backend` con **sesión persistente** y un *client id* fijo. Así, lo que publiquen los wearables mientras el backend está caído queda en el broker (hasta 1000 mensajes) y llega al reconectar.
- **Suscripción:** `device/+/telemetria` y `device/+/estado`, con QoS 1.
- **Configuración:** `Mqtt__Habilitado`, `Mqtt__Servidor`, `Mqtt__Puerto`, `Mqtt__Usuario` y `Mqtt__Clave`. La clave es `MQTT_BACKEND_CLAVE` de `broker/.env`.
- **Código del dispositivo:** el del JSON debe coincidir con el del tópico; si no, el mensaje se ignora. El ACL del broker ya impide que un wearable publique en el tópico de otro.
- **Mensajes malformados:** se registran en el log y no detienen el receptor.

Con QoS 1, **el mismo mensaje puede llegar dos veces**: por ejemplo, si el PUBACK se pierde y el ESP32 reenvía desde su búfer. Por eso el backend deduplica por `(dispositivo, ts)`: una lectura repetida se ignora sin error y la ingesta es idempotente.

## Mensaje

```json
{
  "dispositivo": "ESP32-001",
  "ts": "2026-09-30T14:05:10Z",
  "seq": 18234,
  "fc": 82,
  "fr": 16,
  "spo2": 97,
  "temp": 36.8,
  "pas": 118,
  "pad": 76,
  "caida": false,
  "bateria": 87
}
```

| Campo | Tipo | Obligatorio | Unidad | Descripción |
|---|---|:---:|---|---|
| `dispositivo` | texto | **sí** | — | Código del dispositivo. Se normaliza a mayúsculas |
| `ts` | texto o entero | **sí** | — | Momento de la **medición** (no el del envío). Ver [Marca de tiempo](#marca-de-tiempo) |
| `seq` | entero ≥ 0 | **sí** | — | Contador creciente del dispositivo. Se guarda para trazabilidad (detectar reinicios o huecos) |
| `fc` | número | no | lpm | Frecuencia cardíaca |
| `fr` | número | no | rpm | Frecuencia respiratoria |
| `spo2` | número | no | % | Saturación de oxígeno |
| `temp` | número | no | °C | Temperatura. Se guarda con un decimal |
| `pas` | número | no | mmHg | Presión arterial sistólica |
| `pad` | número | no | mmHg | Presión arterial diastólica |
| `caida` | booleano | no | — | `true` si el acelerómetro detectó una caída. Si falta, es `false` |
| `bateria` | número | no | % | Batería del wearable (0 a 100) |

- **Todas las variables son opcionales**, porque cada sensor puede fallar por separado: si el sensor de temperatura no mide, se omite `temp`. Un campo con `null` es lo mismo que un campo ausente.
- **Lo que envía hoy el wearable de Vitalify:** `fc` (del ECG si los electrodos dan un ritmo confiable; si no, del PPG), `spo2`, `temp` y `caida`.
  - Sin dedo no van `fc` ni `spo2`, salvo que la FC venga del ECG; sin sonda no va `temp`; sin paciente conectado no se envía nada.
  - **No mide `fr`, `pas`, `pad` ni `bateria`**: el contrato los admite para hardware futuro. Para NEWS2 y MEWS se completan con observaciones del personal clínico (ver `docs/motor-clinico.md`).
  - `seq` vuelve a 0 cuando el ESP32 se reinicia.

Ejemplos reales, generados por `construirJsonTelemetria` del firmware:

```json
{"dispositivo":"ESP32-001","ts":"2026-09-30T14:05:10.250Z","seq":18234,"fc":82,"spo2":97,"temp":36.8,"caida":false}
{"dispositivo":"ESP32-001","ts":"2026-09-30T14:05:10.250Z","seq":5,"temp":-0.5,"caida":true}
```

`ContratoFirmwareTests` (backend) prueba que esos mismos JSON se interpretan sin errores. `IotMqttTests` los publica en un Mosquitto real y verifica que se registren.
- **Redondeo:** FC, FR, SpO2, PAS, PAD y batería se redondean a entero; la temperatura, a un decimal.
- **Campos extra:** se ignoran. Un campo conocido con un tipo incorrecto (por ejemplo `"fc": "82"`) invalida el mensaje.

### Marca de tiempo

- **Formatos aceptados:**
  - texto ISO 8601 en UTC, por ejemplo `"2026-09-30T14:05:10Z"` o `"2026-09-30T14:05:10.250Z"`. También se acepta un desfase (`-05:00`), que se convierte a UTC; sin zona, se asume UTC;
  - entero con el **epoch en milisegundos**, por ejemplo `1790777110250`.
- **Precisión:** se trunca a milisegundos. Dos mediciones distintas del mismo dispositivo **no pueden compartir `ts`**, porque se tomarían como duplicadas.
- **Hora del ESP32:** debe sincronizarse por NTP.

## Qué hace el backend con cada lectura

Las reglas se aplican en este orden:

1. **Marca de tiempo.** Se rechaza la lectura si `ts` está más de **2 minutos en el futuro**, o si es más antigua que **`Telemetria__HorasMaximasRetraso`** (24 h por defecto). Dentro de ese margen se acepta, porque puede venir del búfer offline del ESP32. Incidencia: `MarcaDeTiempoInvalida`.
2. **Dispositivo.** Si el código no está registrado, se registra `DispositivoDesconocido`. Si está registrado pero no vinculado a una hospitalización activa, se registra `DispositivoSinVincular`. En ninguno de los dos casos se guarda la lectura.
   - La resolución dispositivo → hospitalización se guarda en caché durante **15 s** (`Telemetria__SegundosCacheDispositivo`).
   - Vincular, liberar o egresar invalidan esa caché en la misma instancia. Con varias instancias, un vínculo nuevo o liberado tarda como máximo 15 s en reflejarse en las demás.
3. **Duplicados.** Si ya existe una lectura con el mismo `(dispositivo, ts)`, el resultado es `Duplicada`: no se guarda otra fila ni se registra ninguna incidencia.
4. **Plausibilidad fisiológica.** Una variable fuera de rango se descarta sola, con la incidencia `FueraDeRango`, y las demás variables válidas de la misma lectura se guardan. Los rangos no son clínicos: sirven para detectar errores del sensor.

   | Variable | Rango válido (por defecto) | Configuración |
   |---|---|---|
   | FC | 20 a 250 lpm | `Telemetria__Rangos__Fc__Minimo`, `...__Maximo` |
   | FR | 4 a 60 rpm | `Telemetria__Rangos__Fr__...` |
   | SpO2 | 50 a 100 % | `Telemetria__Rangos__Spo2__...` |
   | Temperatura | 30.0 a 43.0 °C | `Telemetria__Rangos__Temperatura__...` |
   | PAS | 50 a 260 mmHg | `Telemetria__Rangos__Pas__...` |
   | PAD | 20 a 160 mmHg, y además PAS > PAD | `Telemetria__Rangos__Pad__...` |

5. **Presión.** Es un par. Si falta PAS o PAD, o si PAS ≤ PAD:
   - se descartan las dos;
   - se registra la incidencia `PresionIncompleta` con `requiere_nueva_lectura = true`;
   - se pide una nueva lectura al sensor por el puerto `ISolicitudNuevaLectura` (ver [Comando de nueva lectura](#comando-de-nueva-lectura)).

   Si PAS o PAD está fuera de rango, también se descarta el par, con la incidencia `FueraDeRango`.
6. **Sin nada válido.** Si no queda ninguna variable válida y no hay `caida`, no se guarda la lectura, solo las incidencias.
7. **Guardado.** En una transacción se guardan:
   - la lectura, en `vitalify_historial.lectura_signos`;
   - las incidencias;
   - el estado actual del paciente (`estado_signos_actual`), donde cada variable **solo avanza si su medición es más reciente**. Así, una lectura atrasada del búfer queda en el historial, pero no pisa un valor más nuevo.

   Después se publica el evento `LecturaRegistrada`, que la fase 4 usará para calcular NEWS2 y MEWS.

### Resultado

| Estado | Significado |
|---|---|
| `Aceptada` | Se guardó con todas sus variables |
| `AceptadaParcial` | Se guardó, pero se descartaron algunas variables (vienen en `incidencias`) |
| `Duplicada` | Ya existía: se ignoró sin error |
| `Rechazada` | No se guardó (el motivo viene en `incidencias`) |

## Estado de las variables al consultarlas

`GET /api/v1/pacientes/{id}/signos/actual` devuelve, por cada variable, su último valor válido, `medidoEn` y un `estado` que se calcula al leer:

| Estado | Condición |
|---|---|
| `vigente` | Se midió hace `Telemetria__SegundosVigencia` (90 s) o menos |
| `pendiente-actualizacion` | La última medición es más antigua: se mantiene el valor, pero ya no está al día (HU11 E2) |
| `sin-datos` | Nunca llegó un valor válido de esa variable |

La señal del sensor (`senal`, también en `GET /pacientes/monitoreados`) puede ser:

| Señal | Condición |
|---|---|
| `con-datos` | Llegó alguna lectura en los últimos 2 × la vigencia (180 s) |
| `sin-senal` | No llega ninguna lectura en ese tiempo |
| `sin-datos` | Nunca llegó una lectura |

## Comando de nueva lectura

Hoy el adaptador de `ISolicitudNuevaLectura` solo lo escribe en el log. La tabla de incidencias ya lo registra con `requiere_nueva_lectura = true`.

Solo se dispara con la presión incompleta, y el wearable actual no mide presión, así que con este hardware no ocurre. El ACL del broker ya reserva el tópico de comandos (el backend escribe y cada wearable lee el suyo), pero el firmware todavía no se suscribe. La siguiente es una **propuesta**:

- Tópico: `device/{codigo}/comandos`, QoS 1.
- Mensaje: `{"accion": "repetir-medicion", "variable": "presion", "motivo": "..."}`.

## Probarlo con el wearable real

1. **Broker:** `cd IOT/tesis_V01/broker && docker compose up -d`.
2. **Backend:** en su `.env`, `Mqtt__Habilitado=true`, `Mqtt__Usuario=vitalify-backend` y `Mqtt__Clave` igual a `MQTT_BACKEND_CLAVE` de `broker/.env`. Después, `dotnet run --project src/Vitalify.Api`. En `/health` aparece `mqtt: Healthy`.
3. **Wearable:** en la misma red Wi-Fi que la laptop. `ESP32-001` debe estar registrado y vinculado a un paciente activo.
4. **Verificar:** `GET /api/v1/pacientes/{id}/signos/actual`, `.../riesgo` y `/pacientes/monitoreados` (con `conexionSensor: "en-linea"`).

## Probarlo sin hardware (solo en Development)

**Opción 1: el endpoint de desarrollo** (necesita un token de administrador):

```bash
curl -X POST http://localhost:5080/api/v1/dev/telemetria \
  -H "Authorization: Bearer <accessToken de administrador>" -H "Content-Type: application/json" \
  -d '{"dispositivo":"ESP32-001","ts":"2026-09-30T14:05:10Z","seq":1,"fc":82,"fr":16,"spo2":97,"temp":36.8,"pas":118,"pad":76}'
```

Responde `{ "estado": "Aceptada", "codigoDispositivo": "...", "medidoEn": "...", "incidencias": [] }`, o un 400 con un error por cada campo inválido del contrato. La ruta no existe fuera de Development.

**Opción 2: el simulador.** Con `Simulador__Habilitado=true`, cada `Simulador__IntervaloSegundos` (10 por defecto, mínimo 5) envía una lectura por cada dispositivo vinculado, con el escenario de `Simulador__Escenarios__{codigo}`:

| Escenario | Qué genera |
|---|---|
| `Estable` (por defecto) | Valores normales con ruido pequeño |
| `Deterioro` | Empeora gradualmente durante `Simulador__MinutosDeterioro` (30) |
| `Caida` | Estable, con `caida=true` cada `Simulador__LecturasEntreCaidas` (18) lecturas |
| `SensorDefectuoso` | FC 400 cada 4 lecturas, presión sin diastólica cada 5 y sin temperatura en las lecturas 6 a 17 de cada 24 |

La semilla (`Simulador__Semilla`) hace que las secuencias sean reproducibles.
