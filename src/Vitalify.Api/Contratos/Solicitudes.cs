using System.Text.Json;
using System.Text.Json.Serialization;
using Vitalify.Application.Clinica;

namespace Vitalify.Api.Contratos;

/// <summary>Credenciales para iniciar sesión.</summary>
/// <param name="Correo">Correo registrado.</param>
/// <param name="Contrasena">Contraseña.</param>
public sealed record LoginSolicitud(string Correo, string Contrasena);

/// <summary>Refresh token recibido en el login o en el último refresh.</summary>
public sealed record RefrescarSolicitud(string RefreshToken);

/// <summary>Opcionalmente, el refresh token de la sesión a cerrar.</summary>
public sealed record CerrarSesionSolicitud(string? RefreshToken);

/// <summary>Datos de la cuenta a registrar.</summary>
/// <param name="Nombre">Nombre completo.</param>
/// <param name="Correo">Correo, único en el sistema.</param>
/// <param name="Contrasena">Al menos 8 caracteres, con al menos una letra y un número.</param>
/// <param name="Rol"><c>Medico</c> o <c>Enfermera</c>.</param>
/// <param name="ServicioId">Servicio donde trabaja (obligatorio; ver <c>GET /servicios</c>).</param>
public sealed record RegistrarUsuarioSolicitud(string Nombre, string Correo, string Contrasena, string Rol, Guid? ServicioId = null);

/// <summary>Cama del catálogo.</summary>
/// <param name="Codigo">Código único, por ejemplo <c>MED-B-05</c>.</param>
/// <param name="ServicioId">Servicio de la cama (ver <c>GET /servicios</c>): sus alertas van al personal de ese servicio.</param>
public sealed record RegistrarCamaSolicitud(string Codigo, Guid? ServicioId);

/// <summary>Wearable ESP32.</summary>
/// <param name="Codigo">Código único del tópico MQTT <c>device/{codigo}/telemetria</c>, por ejemplo <c>ESP32-001</c>.</param>
/// <param name="Descripcion">Opcional.</param>
public sealed record RegistrarDispositivoSolicitud(string Codigo, string? Descripcion);

/// <param name="Estado"><c>Mantenimiento</c>, <c>DadoDeBaja</c> o <c>Disponible</c> (reactivar).</param>
public sealed record CambiarEstadoDispositivoSolicitud(string Estado);

/// <summary>Ingreso de un paciente (HU05).</summary>
/// <param name="NombreCompleto">Entre 3 y 150 caracteres.</param>
/// <param name="TipoDocumento"><c>Dni</c>, <c>CarneExtranjeria</c> o <c>Pasaporte</c>.</param>
/// <param name="NumeroDocumento">DNI de 8 dígitos; los demás, de 6 a 12 letras o números.</param>
/// <param name="FechaNacimiento"><c>AAAA-MM-DD</c>. Si no se conoce, enviar <c>edad</c>.</param>
/// <param name="Edad">De 0 a 120. Se estima el nacimiento el 1 de enero del año correspondiente.</param>
/// <param name="CamaId">Cama activa y libre (ver <c>GET /camas?soloDisponibles=true</c>).</param>
/// <param name="DiagnosticoIngreso">Entre 3 y 500 caracteres.</param>
public sealed record RegistrarIngresoSolicitud(
    string NombreCompleto, string TipoDocumento, string NumeroDocumento, string? FechaNacimiento, int? Edad, Guid? CamaId,
    string DiagnosticoIngreso);

/// <summary>Corrección de los datos básicos del paciente y del diagnóstico de su hospitalización activa.</summary>
/// <param name="NombreCompleto">Entre 3 y 150 caracteres.</param>
/// <param name="FechaNacimiento"><c>AAAA-MM-DD</c>. Si no se conoce, enviar <c>edad</c>.</param>
/// <param name="Edad">De 0 a 120.</param>
/// <param name="DiagnosticoIngreso">Entre 3 y 500 caracteres.</param>
/// <param name="ConfusionBasal">
/// Opcional: true si vive con confusión previa (demencia, por ejemplo). El asistente de conciencia preguntará
/// "¿más confundido que de costumbre?". Si no se envía, no cambia.
/// </param>
public sealed record ActualizarPacienteSolicitud(
    string NombreCompleto, string? FechaNacimiento, int? Edad, string DiagnosticoIngreso, bool? ConfusionBasal = null);

/// <param name="DispositivoId">Sensor disponible (ver <c>GET /dispositivos?estado=Disponible</c>).</param>
public sealed record VincularDispositivoSolicitud(Guid? DispositivoId);

/// <summary>
/// Nivel de conciencia. Acepta un texto (formato de la fase 4: <c>"Alerta"</c>, <c>"ConfusionNueva"</c>,
/// <c>"RespondeVoz"</c>, <c>"RespondeDolor"</c> o <c>"NoResponde"</c>) o un objeto con las respuestas del asistente:
/// <c>{ "despiertoCoherente": true, "confusionNueva": false }</c>. Con respuestas, el backend calcula el ACVPU y
/// <b>gana el calculado</b> aunque también llegue <c>nivel</c>.
/// </summary>
[JsonConverter(typeof(ConvertidorConciencia))]
public sealed record ConcienciaSolicitud(
    string? Nivel = null, bool? DespiertoCoherente = null, bool? ConfusionNueva = null, bool? RespondeVoz = null, bool? RespondeDolor = null)
{
    public ConcienciaTomada AComando() => new(Nivel, DespiertoCoherente, ConfusionNueva, RespondeVoz, RespondeDolor);
}

/// <summary>
/// Toma manual: lo que el wearable no mide (presión, conciencia, oxígeno), la FR si el sensor no tiene una vigente y la
/// temperatura de un termómetro clínico. Ver <c>GET /api/v1/guias/toma-manual</c>.
/// </summary>
/// <param name="Fr">Frecuencia respiratoria (rpm), contada en 60 s. Solo si la del sensor no está vigente.</param>
/// <param name="Pas">Formato de la fase 4: sistólica (mmHg), con <c>pad</c>. Con el asistente, use <c>presion</c>.</param>
/// <param name="Pad">Formato de la fase 4: diastólica (mmHg), con <c>pas</c>.</param>
/// <param name="Temperatura">°C, de un termómetro clínico.</param>
/// <param name="Conciencia">Texto (fase 4) u objeto con las respuestas del asistente.</param>
/// <param name="OxigenoSuplementario">Formato de la fase 4: true si recibe oxígeno. Con el asistente, use <c>oxigeno</c>.</param>
/// <param name="ObservadaEn">Momento de la medición (UTC); si falta, ahora. Hasta 4 h atrás.</param>
/// <param name="VersionGuia">Versión de la guía con la que la app armó el asistente (por ejemplo <c>2026.11.1</c>).</param>
/// <param name="Oxigeno"><c>{ recibe, dispositivo, flujoLpm, fio2, objetivo88a92 }</c>.</param>
/// <param name="Presion"><c>{ checklist, posicion, brazo, metodo, lecturas: [{ pas, pad, medidaEn }] }</c>, de 1 a 3 lecturas.</param>
/// <param name="Contexto"><c>{ dolor (0–10), ansiedadOEsfuerzoReciente, antipireticoUltimas4h, betabloqueadores }</c>. No suma puntos.</param>
/// <param name="PreocupacionClinica">"¿Le preocupa este paciente aunque el puntaje sea bajo?"</param>
public sealed record RegistrarObservacionSolicitud(
    int? Fr = null, int? Pas = null, int? Pad = null, decimal? Temperatura = null, ConcienciaSolicitud? Conciencia = null,
    bool? OxigenoSuplementario = null, DateTime? ObservadaEn = null, string? VersionGuia = null, OxigenoTomado? Oxigeno = null,
    PresionTomada? Presion = null, ContextoTomado? Contexto = null, bool? PreocupacionClinica = null)
{
    public TomaManualComando AComando() => new()
    {
        Fr = Fr,
        Pas = Pas,
        Pad = Pad,
        Temperatura = Temperatura,
        Conciencia = Conciencia?.AComando(),
        OxigenoSuplementario = OxigenoSuplementario,
        Oxigeno = Oxigeno,
        Presion = Presion,
        Contexto = Contexto,
        PreocupacionClinica = PreocupacionClinica,
        VersionGuia = VersionGuia,
        ObservadaEn = ObservadaEn,
    };
}

/// <param name="Escala"><c>Escala1</c> o <c>Escala2</c> (insuficiencia respiratoria hipercápnica, objetivo 88–92 %).</param>
/// <param name="Motivo">Por qué se indica (de 3 a 300 caracteres). Queda en la hospitalización, no en la auditoría.</param>
public sealed record CambiarEscalaSpo2Solicitud(string? Escala, string? Motivo);

/// <param name="Qr">Contenido del QR que muestra el wearable, por ejemplo <c>VTF1:ESP32-001:K7P3QX9M</c>.</param>
public sealed record EscanearQrSolicitud(string? Qr);

/// <summary>Paciente de la vinculación: <c>pacienteId</c> de uno existente o los datos de uno nuevo (como en <c>POST /pacientes</c>).</summary>
/// <param name="PacienteId">Paciente ya registrado y sin hospitalización activa. Si se envía, los demás datos se ignoran.</param>
/// <param name="NombreCompleto">Entre 3 y 150 caracteres.</param>
/// <param name="NumeroDocumento">DNI de 8 dígitos; los demás, de 6 a 12 letras o números.</param>
/// <param name="Edad">De 0 a 120, si no se conoce la fecha.</param>
/// <param name="TipoDocumento"><c>Dni</c>, <c>CarneExtranjeria</c> o <c>Pasaporte</c>.</param>
/// <param name="FechaNacimiento"><c>AAAA-MM-DD</c>. Si no se conoce, enviar <c>edad</c>.</param>
/// <param name="ConfusionBasal">true si vive con confusión previa.</param>
public sealed record PacienteVinculacionSolicitud(
    Guid? PacienteId = null, string? NombreCompleto = null, string? TipoDocumento = null, string? NumeroDocumento = null,
    string? FechaNacimiento = null, int? Edad = null, bool? ConfusionBasal = null);

/// <summary>Cierra la vinculación por QR: paciente, cama, diagnóstico y toma inicial.</summary>
/// <param name="TokenVinculacion">El que devolvió <c>POST /vinculaciones/escaneo</c> (vale 5 minutos).</param>
/// <param name="Paciente">Paciente existente o nuevo.</param>
/// <param name="CamaId">Cama activa y libre.</param>
/// <param name="DiagnosticoIngreso">Entre 3 y 500 caracteres.</param>
/// <param name="ObservacionInicial">
/// Mismo formato que <c>POST /pacientes/{id}/observaciones</c>. Obligatorios: oxígeno (sí/no), conciencia (respuestas) y
/// al menos una lectura de presión.
/// </param>
public sealed record ConfirmarVinculacionSolicitud(
    string? TokenVinculacion, PacienteVinculacionSolicitud? Paciente, Guid? CamaId, string? DiagnosticoIngreso,
    RegistrarObservacionSolicitud? ObservacionInicial);

/// <param name="Motivo"><c>AltaMedica</c>, <c>Traslado</c>, <c>Fallecimiento</c>, <c>Voluntaria</c> u <c>Otro</c>.</param>
/// <param name="Observacion">Opcional, hasta 500 caracteres.</param>
public sealed record RegistrarEgresoSolicitud(string Motivo, string? Observacion);

/// <summary>Lee <c>conciencia</c> como texto (fase 4) o como objeto con las respuestas del asistente.</summary>
public sealed class ConvertidorConciencia : JsonConverter<ConcienciaSolicitud>
{
    public override ConcienciaSolicitud? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new ConcienciaSolicitud(Nivel: reader.GetString());
            case JsonTokenType.StartObject:
                var respuestas = JsonSerializer.Deserialize<RespuestasConcienciaJson>(ref reader, options);
                return respuestas is null
                    ? null
                    : new ConcienciaSolicitud(
                        respuestas.Nivel, respuestas.DespiertoCoherente, respuestas.ConfusionNueva, respuestas.RespondeVoz, respuestas.RespondeDolor);
            default:
                throw new JsonException("conciencia debe ser un texto (Alerta, ConfusionNueva...) o un objeto con las respuestas del asistente.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ConcienciaSolicitud value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(
            writer,
            new RespuestasConcienciaJson(value.Nivel, value.DespiertoCoherente, value.ConfusionNueva, value.RespondeVoz, value.RespondeDolor),
            options);

    /// <summary>Mismo contenido sin el convertidor, para no entrar en recursión.</summary>
    private sealed record RespuestasConcienciaJson(
        string? Nivel, bool? DespiertoCoherente, bool? ConfusionNueva, bool? RespondeVoz, bool? RespondeDolor);
}

/// <summary>Servicio hospitalario.</summary>
/// <param name="Codigo">Por ejemplo <c>EMERGENCIA</c>, <c>UVI</c> o <c>MEDICINA</c> (2 a 30 letras, números o guiones).</param>
/// <param name="Nombre">Por ejemplo "Emergencia".</param>
public sealed record RegistrarServicioSolicitud(string Codigo, string Nombre);

/// <param name="ServicioId">Servicio de la enfermera o del médico.</param>
public sealed record AsignarServicioSolicitud(Guid? ServicioId);

/// <summary>
/// Un día de la planilla: <c>dia</c> y, por cada turno (<c>GuardiaDiurna</c>, <c>GuardiaNocturna</c>, <c>Manana</c>,
/// <c>Tarde</c>), las letras que lo cubren. Por ejemplo <c>{ "dia": 1, "GuardiaDiurna": ["C"], "GuardiaNocturna": ["A"] }</c>.
/// </summary>
public sealed class DiaPlanillaSolicitud
{
    public int Dia { get; init; }

    /// <summary>Los turnos del día (cada propiedad que no es <c>dia</c>).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Turnos { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> TurnosComoLetras() =>
        (Turnos ?? []).ToDictionary(
            t => t.Key,
            t => (IReadOnlyList<string>)(t.Value.ValueKind == JsonValueKind.Array
                ? t.Value.EnumerateArray().Select(l => l.ValueKind == JsonValueKind.String ? l.GetString() ?? string.Empty : l.ToString()).ToList()
                : [t.Value.ToString()]));
}

/// <summary>
/// La planilla del mes, con el formato del papel: cada enfermera tiene una letra y cada día dice qué letra cubre cada
/// turno. Reemplaza la programación del mes completo.
/// </summary>
/// <param name="Letras">Letra → id de la enfermera, por ejemplo <c>{ "A": "…", "B": "…" }</c>.</param>
/// <param name="Dias">Los días del mes con sus turnos.</param>
public sealed record CargarProgramacionSolicitud(Dictionary<string, Guid>? Letras, List<DiaPlanillaSolicitud>? Dias);

/// <param name="Nota">Qué se hizo (obligatoria, de 5 a 1000 caracteres).</param>
public sealed record AtenderAlertaSolicitud(string? Nota);

/// <summary>Token de notificaciones push de la app.</summary>
/// <param name="Token">Token de registro de FCM (<c>FirebaseMessaging.instance.getToken()</c>).</param>
/// <param name="Plataforma"><c>Android</c>, <c>Ios</c> o <c>Web</c>.</param>
public sealed record DispositivoMovilSolicitud(string? Token, string? Plataforma);
