namespace Vitalify.Domain.Telemetria;

/// <summary>
/// Problema técnico detectado al recibir telemetría (log de incidencias). Solo datos técnicos: nunca el nombre ni
/// el documento del paciente.
/// </summary>
public sealed class IncidenciaTelemetria
{
    public const int LargoMaximoCodigo = 64;
    public const int LargoMaximoDetalle = 500;

    public Guid Id { get; private set; }
    public DateTime OcurridaEn { get; private set; }
    public string CodigoDispositivo { get; private set; } = null!;
    public Guid? HospitalizacionId { get; private set; }
    public TipoIncidencia Tipo { get; private set; }
    public VariableSigno? Variable { get; private set; }
    public decimal? ValorRecibido { get; private set; }
    public string Detalle { get; private set; } = null!;
    public bool RequiereNuevaLectura { get; private set; }

    private IncidenciaTelemetria()
    {
    }

    public static IncidenciaTelemetria Registrar(
        DateTime ocurridaEn, string codigoDispositivo, Guid? hospitalizacionId, TipoIncidencia tipo, string detalle,
        VariableSigno? variable = null, decimal? valorRecibido = null, bool requiereNuevaLectura = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            OcurridaEn = ocurridaEn,
            CodigoDispositivo = Recortar(codigoDispositivo, LargoMaximoCodigo),
            HospitalizacionId = hospitalizacionId,
            Tipo = tipo,
            Variable = variable,
            ValorRecibido = valorRecibido,
            Detalle = Recortar(detalle, LargoMaximoDetalle),
            RequiereNuevaLectura = requiereNuevaLectura,
        };

    public static IncidenciaTelemetria Desde(DescarteSigno descarte, DateTime ocurridaEn, string codigoDispositivo, Guid? hospitalizacionId) =>
        Registrar(ocurridaEn, codigoDispositivo, hospitalizacionId, descarte.Tipo, descarte.Detalle,
            descarte.Variable, descarte.Valor, descarte.RequiereNuevaLectura);

    private static string Recortar(string? valor, int largo) =>
        string.IsNullOrEmpty(valor) ? string.Empty : valor.Length > largo ? valor[..largo] : valor;
}
