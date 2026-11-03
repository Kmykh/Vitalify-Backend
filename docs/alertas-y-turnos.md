# Alertas, notificaciones y turnos (fase 6)

Esta fase cubre la HU15, la HU16, la HU20, la parte del backend de la HU21 y la HU23. Con ella, el backend:

- genera las **alertas clínicas** a partir del motor NEWS2/MEWS, del wearable y de las tomas manuales;
- avisa a **quien está de turno**, en tiempo real (SignalR) y por push (FCM);
- **escala** las alertas que nadie toma;
- arma la **entrega de turno**.

Todo funciona sin el wearable: con el simulador (`DeterioroRapido`), el endpoint de desarrollo o un Mosquitto local.

## Servicios y turnos

### Servicios

Un servicio es una unidad del hospital (por ejemplo, Medicina B). Las camas, las enfermeras y los médicos pertenecen a uno.

- Tabla `vitalify.servicio` (`codigo` único, en mayúsculas, de 2 a 30 caracteres `A-Z`, `0-9` o `-`).
- `cama.servicio_id` reemplaza al texto `cama.servicio`. La migración `Fase6_ServiciosYTurnos` crea un servicio por cada texto distinto que había y mueve las camas a él.
- `usuario.servicio_id` es obligatorio al registrar una enfermera o un médico. El administrador lo cambia con `PUT /usuarios/{id}/servicio`.

### Tipos de turno

| Tipo | Horario (hora de Lima) | Configuración |
|---|---|---|
| `GuardiaDiurna` | 07:00–19:00 | `Turnos__Tipos__GuardiaDiurna__Inicio` / `__Fin` |
| `GuardiaNocturna` | 19:00–07:00 del día siguiente | `Turnos__Tipos__GuardiaNocturna__*` |
| `Manana` | 07:00–13:00 | `Turnos__Tipos__Manana__*` |
| `Tarde` | 13:00–19:00 | `Turnos__Tipos__Tarde__*` |

- La zona es `Turnos__ZonaHoraria` (`America/Lima`). Si el sistema no tiene la base de zonas, se usa UTC−5 fijo, porque Perú no tiene horario de verano.
- Los turnos son intervalos `[inicio, fin)`: a las 07:00 ya manda el turno que entra. Dos turnos que solo se tocan (una nocturna que termina a las 07:00 y una diurna que empieza a las 07:00) **no** se cruzan.
- Una nocturna pertenece al día en que empieza: la nocturna del día 5 va del 5 a las 19:00 al 6 a las 07:00.

### Programación mensual (la planilla)

`PUT /api/v1/servicios/{id}/programacion/{anio}/{mes}` (administrador) carga el mes completo y reemplaza lo que hubiera. Usa el formato de la planilla de papel: una letra por enfermera y, por cada día, qué letras cubren cada turno.

```json
{
  "letras": {
    "A": "8c6f...-id de la enfermera A",
    "B": "1d2e...-id de la enfermera B",
    "C": "9a7b...-id de la enfermera C"
  },
  "dias": [
    { "dia": 1, "GuardiaDiurna": ["A"], "GuardiaNocturna": ["B"] },
    { "dia": 2, "GuardiaDiurna": ["B"], "GuardiaNocturna": ["C"] },
    { "dia": 3, "Manana": ["C"], "Tarde": ["A"], "GuardiaNocturna": ["A"] }
  ]
}
```

Validaciones (400, con un error por día y letra):

| Clave del error | Motivo |
|---|---|
| `letras.B` | La letra no corresponde a una enfermera activa del servicio |
| `dias.31` | El día no existe en ese mes, o está repetido |
| `dias.3.D` | La letra no está en `letras` |
| `dias.3.Noche` | El turno no existe (los válidos son `GuardiaDiurna`, `GuardiaNocturna`, `Manana` y `Tarde`) |
| `dias.3.A` | La enfermera A tiene dos turnos que se cruzan (por ejemplo, `GuardiaDiurna` y `Tarde` el mismo día) |

Ejemplo de cruce: si el día 3 la letra `D` estuviera en `GuardiaDiurna` (07–19) y en `Tarde` (13–19), la carga daría 400 en `dias.3.D`.

La carga se audita como `ProgramacionCargada`, solo con el servicio, el mes y la cantidad de turnos. `GET` en la misma ruta devuelve la planilla con las letras y los nombres.

### Quién está de turno

- `GET /api/v1/turnos/actual?servicioId=` devuelve las enfermeras de turno en este momento, con su letra, el tipo de turno y el intervalo en UTC.
- `GET /api/v1/turnos/mios?desde=&hasta=` devuelve los turnos de la enfermera que consulta, como máximo 62 días.
- El puerto `ITurnoVigente.EnfermerasDeTurnoAsync(servicioId, instante)` decide a quién se notifica. **Respaldo** si nadie está programado: todas las enfermeras activas del servicio y, si el servicio no tiene ninguna, todas las enfermeras activas. En ese caso responde `esRespaldo: true` y escribe una advertencia en el log, solo con el id del servicio.

## Alertas

### Modelo

Tabla `vitalify.alerta_clinica`:

- **Qué y de quién:** `id`, `hospitalizacion_id`, `paciente_id`, `servicio_id`, `tipo`, `prioridad`, `estado`, `nivel_riesgo`, `news2`, `mews` y `detalle` (jsonb, sin datos personales).
- **Tiempos:** `creada_en`, `actualizada_en`, `tomada_por`, `tomada_en`, `atendida_por`, `atendida_en`, `nota_atencion` y `escalada_en`.
- **Notificaciones:** `notificada_a` (jsonb).

Hay **una sola alerta abierta** (`Pendiente` o `EnAtencion`) por hospitalización y tipo: lo garantiza el índice único parcial `ux_alerta_abierta`. Si dos lecturas llegan a la vez, la segunda actualiza la alerta que creó la primera.

### Cuándo se crea

| Tipo | Disparador | Prioridad | A quién se avisa |
|---|---|---|---|
| `riesgo` | Evaluación con NEWS2 `bajo-medio`, `medio` o `alto`, o MEWS `medio` o `alto`. Se toma el mayor | bajo-medio → baja · medio → media · alto → alta | Enfermeras de turno. Si es alta, también los médicos del servicio |
| `caida` | Lectura con `caida: true` | alta | Enfermeras de turno |
| `sin-senal` | Wearable vinculado sin lecturas en `Alertas__SegundosSinSenal` (180 s), o `offline` en el broker | media | Enfermeras de turno |
| `toma-pendiente` | Venció la toma manual: pasaron `proximaTomaManualEn` y `Clinico__MinutosToleranciaToma` (ver [motor-clinico.md](motor-clinico.md#vigencia-de-la-toma-manual)) | baja | Enfermeras de turno |
| `preocupacion-clinica` | Observación con `preocupacionClinica: true` | media | Médicos del servicio |

Si el servicio no tiene médicos activos, se avisa a todos los médicos activos.

**Si ya hay una alerta abierta del mismo tipo:**
- si **sube** la prioridad o el nivel, se actualiza y se vuelve a notificar (motivo `subio`);
- si baja o se mantiene, solo se actualizan los valores y las pantallas (motivo `estado`), sin hacer sonar a nadie. La prioridad nunca baja.

**Cierre:**
- `riesgo`, `caida` y `preocupacion-clinica` **no se cierran solas**: las cierra una persona.
- `sin-senal` se cierra sola cuando vuelve una lectura, y `toma-pendiente`, cuando se registra la toma. En ambos casos queda `nota_atencion = "Resuelta automáticamente"`.
- **Espera tras atender (fase 7):** al atender una alerta de `riesgo`, durante `Alertas__MinutosEsperaTrasAtender` (30 min) no se abre otra del mismo tipo, salvo que **suba** la prioridad o el nivel respecto de la atendida. Al terminar la espera, si el paciente sigue en el mismo nivel, sí se abre una nueva: el problema continúa. Sin esta regla, la evaluación siguiente (10 s después) reabría la alerta mientras el equipo ya estaba actuando. Con 0 se desactiva.

**Dónde se engancha:**
- Las alertas de riesgo y de preocupación salen del evento `RiesgoEvaluado`, que se publica al guardar cada evaluación (de una lectura o de una observación).
- Las de caída y el cierre de `sin-senal` salen de `LecturaRegistrada`.
- `VigilanteAlertasWorker` corre cada `Alertas__SegundosVigilancia` (30 s; 0 lo apaga) y revisa la falta de señal, las tomas vencidas, el escalamiento y el cambio de turno.
- **Un fallo al notificar nunca rompe la ingesta.** El manejador lo atrapa y lo deja en el log (puerto `IBitacoraFallos`, solo con ids).

### Tomar y atender

| Acción | Endpoint | Resultado |
|---|---|---|
| Tomar | `POST /api/v1/alertas/{id}/tomar` | `en-atencion`. 409 `alerta-tomada-por-otro` si otra persona ya la tomó; 409 `alerta-ya-atendida` si ya se cerró |
| Atender | `POST /api/v1/alertas/{id}/atender` con `{ "nota": "..." }` | `atendida`. La nota es obligatoria, de 5 a 1000 caracteres (si no, 400 en `nota`). Se puede atender sin haberla tomado antes |

- Solo la enfermera o el médico **del servicio de la alerta**; si no, 403 `alerta-de-otro-servicio`. Un usuario sin servicio recibe 403 `usuario-sin-servicio`.
- Se audita `AlertaTomada` y `AlertaAtendida` con el id de la alerta. Cada cambio se avisa a las pantallas con `alertaActualizada` (motivo `estado`).

### Escalamiento

| Prioridad | Si sigue `pendiente` a los… | Se avisa además a |
|---|---|---|
| alta | `Alertas__Escalamiento__MinutosAlta` (5 min) | Los médicos del servicio y **todas** las enfermeras activas del servicio, aunque no estén de turno |
| media | `Alertas__Escalamiento__MinutosMedia` (15 min) | Los médicos del servicio |
| baja | No escala | — |

- Se guarda `escalada_en` y se escala **una sola vez**.
- Una alerta tomada (`en-atencion`) no escala.
- El push empieza con "Sin atender: ".

### Cambio de turno

Cuando empieza un turno (el vigilante detecta los turnos que empezaron desde su vuelta anterior), las alertas abiertas del servicio se avisan a las enfermeras programadas en él. El motivo es `cambio-turno` y el push empieza con "Alerta abierta al iniciar su turno: ".

### Consultas

- `GET /api/v1/alertas?estado=&servicioId=&tipo=&desde=&hasta=&pagina=&tamano=`.
  - Por defecto devuelve las abiertas del servicio del usuario, ordenadas por prioridad y antigüedad.
  - `estado` es `abiertas` (por defecto), `pendiente`, `en-atencion`, `atendida` o `todas`.
  - Otro `servicioId` da 403.
- `GET /api/v1/pacientes/{id}/alertas?hospitalizacionId=`: las alertas de un episodio (por defecto el activo o el último), de la más reciente a la más antigua, con los tiempos de respuesta en segundos (`segundosHastaTomada` y `segundosHastaAtendida`, desde la creación) y la cantidad de notificaciones. Desde la fase 7 es parte del historial: la lee cualquier servicio y queda auditada (ver [historial-clinico.md](historial-clinico.md)).
- `GET /api/v1/pacientes/monitoreados` agrega `alertasAbiertas`: `{ "cantidad": 1, "prioridadMaxima": "alta" }`.

## Notificaciones

### Tiempo real (SignalR)

- **Hub:** `/hubs/monitoreo` (fuera de `/api/v1`). Se autentica con el mismo access token. Como el navegador no puede mandar encabezados en WebSocket, acepta `?access_token=` en el query **solo en esa ruta**. Solo pueden conectarse la enfermera y el médico.
- **Grupos:** al conectarse, el usuario entra a `usuario:{id}` y a `servicio:{servicioId}`; el médico, además, a `medicos:{servicioId}`. Hoy todos los eventos van al grupo del servicio, y los otros dos quedan listos para avisos personales.

**Eventos:**

| Evento | Cuándo | Contenido |
|---|---|---|
| `alertaCreada` | Se abre una alerta | `alertaId`, `tipo`, `prioridad`, `estado`, `pacienteId`, `hospitalizacionId`, `servicioId`, `cama`, `nivelRiesgo`, `news2`, `mews`, `creadaEn`, `actualizadaEn`, `motivo` y `destinatarios` |
| `alertaActualizada` | Sube, escala, se avisa en el cambio de turno, se toma, se atiende o cambian los valores | Igual, con `motivo` = `subio`, `escalamiento`, `cambio-turno` o `estado` |
| `riesgoActualizado` | Cada evaluación | `pacienteId`, `hospitalizacionId`, `cama`, `news2`, `mews`, `nivel`, `completo` y `evaluadaEn` |
| `signosActualizados` | Cada lectura, **como máximo uno cada 5 s por paciente** | `pacienteId`, `hospitalizacionId`, `cama`, `medidoEn`, `fc`, `fr`, `spo2`, `temperatura`, `pas`, `pad` y `caida` |

- **Sin nombres.** Los eventos nunca llevan el nombre, el DNI ni el diagnóstico: el cliente los pide a la API (`GET /pacientes/{id}`) con su permiso.
- **`destinatarios`** son los ids a los que se pide atención. Todas las pantallas del servicio se actualizan, pero solo los destinatarios deberían hacer sonar la alarma.
- **`motivo` = `estado`** solo sincroniza las pantallas: no debe sonar.

En [`docs/ejemplos/cliente-signalr.html`](ejemplos/cliente-signalr.html) hay un cliente de prueba.

### Push (FCM)

- **Registro del token:** la app lo registra con `POST /api/v1/notificaciones/dispositivos` (`{ "token": "...", "plataforma": "Android" }`, `"Ios"` o `"Web"`) y lo quita al cerrar sesión con `DELETE` en la misma ruta, con el mismo cuerpo. Responden 204.
- **Tabla `vitalify.dispositivo_movil`:** guarda el usuario, el token, su SHA-256 (único), la plataforma y el último uso. Si otro usuario registra el mismo token (un celular compartido), el token pasa a ese usuario.
- **Adaptadores:**
  - `NotificadorPushEnLog` (por defecto): escribe el push en el log, sin el token.
  - `NotificadorPushFcm` (paquete `FirebaseAdmin`): se usa con `Push__Habilitado=true` y `Push__RutaCredencialesFirebase`. Si la ruta no existe, la API no arranca.
- **Mensaje:**
  - título "Vitalify · Cama MED-B-01";
  - cuerpo "Riesgo alto (NEWS2 7). Toque para atender" (o "MEWS n" si el nivel lo puso el MEWS);
  - `data`: `alertaId`, `pacienteId` y `tipo`, con prioridad alta en Android.
- **Tokens vencidos:** si FCM responde que un token no está registrado, se borra.

**Credenciales de Firebase:**
1. En la consola de Firebase, ve a *Configuración del proyecto → Cuentas de servicio → Generar nueva clave privada*.
2. Guarda el JSON **fuera del repositorio** (por ejemplo, `~/vitalify/credenciales/firebase.json`).
3. En el `.env`, pon `Push__Habilitado=true` y `Push__RutaCredencialesFirebase=<esa ruta>`.

El `.gitignore` excluye `*firebase*.json`, `*-firebase-adminsdk-*.json` y `credenciales/`. En Docker, monta el archivo como volumen de solo lectura.

### Registro de cada notificación

Cada aviso queda en `notificada_a` de la alerta:

```json
{ "usuarioId": "...", "canal": "push", "motivo": "escalamiento", "en": "2026-11-02T23:08:35Z", "resultado": "enviado" }
```

- `canal`: `tiempo-real` o `push`.
- `resultado`:
  - en tiempo real: `conectado` o `sin-conexion`;
  - en push: `enviado`, `no-registrado`, `error` o `sin-dispositivo`.

Con estos datos y los tiempos de la alerta se mide el tiempo de respuesta en la tesis.

## Entrega de turno

`GET /api/v1/servicios/{id}/entrega-turno` (enfermera o médico del servicio) es el resumen que se revisa en el reporte de enfermería al cambiar de turno.

La ventana es la guardia vigente (07–19 o 19–07). Por cada paciente hospitalizado del servicio, del de mayor riesgo al de menor, trae:

- cama, nivel de riesgo y si la evaluación es completa;
- NEWS2 y MEWS actuales y su tendencia en el turno (`primero`, `maximo` y `ultimo`);
- alertas abiertas y alertas atendidas en el turno;
- `proximaTomaManualEn` y `tomaVencida` (ya pasó la tolerancia);
- sensor: código, señal (como en `signos/actual`), presencia en el broker (`en-linea`, `fuera-de-linea` o `desconocida`) y última lectura;
- última preocupación clínica registrada.

Es la única respuesta de esta fase que trae el nombre del paciente, porque es una consulta del personal clínico autorizado.

## Configuración

| Variable | Por defecto | Qué hace |
|---|---|---|
| `Turnos__ZonaHoraria` | `America/Lima` | Zona de la planilla |
| `Turnos__Tipos__{Tipo}__Inicio` / `__Fin` | ver arriba | Horario de cada turno (`HH:mm`) |
| `Alertas__SegundosSinSenal` | 180 | Segundos sin lecturas para `sin-senal` |
| `Alertas__Escalamiento__MinutosAlta` | 5 | Escalamiento de las alertas altas |
| `Alertas__Escalamiento__MinutosMedia` | 15 | Escalamiento de las alertas medias |
| `Alertas__SegundosVigilancia` | 30 | Intervalo del vigilante (0 lo apaga) |
| `Alertas__MinutosEsperaTrasAtender` | 30 | Tras atender una alerta de riesgo, minutos sin abrir otra del mismo nivel (0 la desactiva) |
| `Clinico__MinutosToleranciaToma` | 30 | Tolerancia de la toma manual |
| `Push__Habilitado` | `false` | Envía el push por FCM; si está apagado, solo va al log |
| `Push__RutaCredencialesFirebase` | — | JSON de la cuenta de servicio, fuera del repositorio |

## Probarlo sin hardware

1. **Base y API:** en el `.env`, `Seed__DatosDemo=true`, `Simulador__Habilitado=true` y `Simulador__Escenarios__ESP32-001=DeterioroRapido`. Después, `dotnet run --project src/Vitalify.Api`. La semilla crea el servicio `MEDICINA-B`, las camas `MED-B-01` a `MED-B-06` y los sensores.
2. **Personal:** con el administrador, `GET /api/v1/servicios` para obtener el id de `MEDICINA-B`. Registra enfermeras y un médico con ese `servicioId` y carga la planilla del mes.
3. **Paciente:** con la enfermera de turno (`GET /turnos/actual`), ingresa un paciente en `MED-B-01` y vincula `ESP32-001`.
4. **Cliente SignalR:** `python3 -m http.server 5500 -d docs/ejemplos` y abre `http://localhost:5500/cliente-signalr.html?api=http://localhost:5080&token=<accessToken de la enfermera>`. El puerto 5500 ya está en los orígenes de CORS de Development.
5. **Qué se ve:**
   - En 1 o 2 minutos NEWS2 llega a 5–6 (`alertaCreada`, media) y luego a 7 o más (`alertaActualizada`, motivo `subio`, alta).
   - El push queda en el log ("Push (solo log)").
   - A los 5 min sin tomarla llega `alertaActualizada` con motivo `escalamiento` y `GET /alertas` muestra `escaladaEn`.
   - `GET /servicios/{id}/entrega-turno` muestra al paciente arriba, en riesgo alto.
