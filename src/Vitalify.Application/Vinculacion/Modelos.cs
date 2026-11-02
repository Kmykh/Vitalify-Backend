using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;

namespace Vitalify.Application.Vinculacion;

/// <summary>Sección <c>Vinculacion</c> de la configuración.</summary>
/// <param name="VigenciaCodigo">Cuánto vale un QR (10 min por defecto).</param>
/// <param name="VigenciaToken">Cuánto tiempo hay para confirmar tras escanear (5 min por defecto).</param>
/// <param name="IntentosMaximos">Códigos equivocados sobre un mismo dispositivo antes de invalidar su QR (5).</param>
/// <param name="IntervaloRenovacion">Cada cuánto el worker renueva los QR vencidos (60 s; 0 lo apaga).</param>
public sealed record OpcionesVinculacion(TimeSpan VigenciaCodigo, TimeSpan VigenciaToken, int IntentosMaximos, TimeSpan IntervaloRenovacion)
{
    public static readonly OpcionesVinculacion PorDefecto =
        new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), 5, TimeSpan.FromSeconds(60));
}

/// <summary>Cama y paciente de un dispositivo vinculado. El nombre solo sirve para calcular las iniciales.</summary>
public sealed record DispositivoEnCama(string CodigoCama, string NombrePaciente);

/// <param name="ExpiraEn">Vencimiento del QR publicado. El código en claro nunca sale por la API: solo lo muestra el wearable.</param>
public sealed record QrGeneradoDto(Guid DispositivoId, string CodigoDispositivo, DateTime ExpiraEn);

public sealed record DispositivoEscaneadoDto(Guid Id, string Codigo);

/// <param name="TokenVinculacion">Se envía en <c>POST /vinculaciones/confirmar</c>; vale unos minutos y una sola vez.</param>
/// <param name="Conexion"><c>establecida</c>: el sensor está en línea y ya muestra "emparejando".</param>
public sealed record EscaneoQrDto(string TokenVinculacion, DispositivoEscaneadoDto Dispositivo, string Conexion, DateTime ExpiraEn);

/// <param name="Riesgo">Primera evaluación NEWS2/MEWS con la observación inicial (null si no se pudo guardar).</param>
/// <param name="Avisos">Avisos de la vinculación (por ejemplo, que la observación inicial no se guardó).</param>
public sealed record VinculacionConfirmadaDto(
    FichaPacienteDto Paciente, DispositivoVinculadoDto Dispositivo, EvaluacionRiesgoDto? Riesgo, IReadOnlyList<string> Avisos);

public static class ErroresVinculacion
{
    /// <summary>El mismo mensaje para un QR vencido, ya usado o inventado: no se revela cuál de los tres fue.</summary>
    public static readonly Error QrNoValido =
        Error.NoEncontrado("qr-no-valido", "El código QR no es válido o ya venció. Pida al sensor un código nuevo y vuelva a escanear.");

    public static readonly Error SensorSinConexion =
        Error.Conflicto("sensor-sin-conexion", "El sensor no está conectado. Verifique que esté encendido y con WiFi, y vuelva a escanear.");

    public static readonly Error VinculacionNoValida =
        Error.NoEncontrado("vinculacion-no-valida", "La vinculación venció o no es válida. Escanee el QR de nuevo.");

    /// <summary>Dos procesos generaron a la vez un QR para el mismo dispositivo: vale el primero.</summary>
    public static readonly Error CodigoConcurrente =
        Error.Conflicto("codigo-vinculacion-concurrente", "Se generó otro código para este sensor al mismo tiempo. Use el que muestra la pantalla.");
}
