namespace Vitalify.Domain.Telemetria;

/// <summary>
/// Último valor válido de cada variable de una hospitalización, con su propia hora de medición. Cada variable solo
/// avanza si la lectura nueva es más reciente que la guardada: una lectura atrasada del búfer offline se agrega
/// al historial, pero no pisa un valor más nuevo.
/// </summary>
public sealed class EstadoSignosActual
{
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }
    public string CodigoDispositivo { get; private set; } = null!;

    public int? FcValor { get; private set; }
    public DateTime? FcMedidoEn { get; private set; }
    public int? FrValor { get; private set; }
    public DateTime? FrMedidoEn { get; private set; }
    public int? Spo2Valor { get; private set; }
    public DateTime? Spo2MedidoEn { get; private set; }
    public decimal? TempValor { get; private set; }
    public DateTime? TempMedidoEn { get; private set; }
    public int? PasValor { get; private set; }
    public DateTime? PasMedidoEn { get; private set; }
    public int? PadValor { get; private set; }
    public DateTime? PadMedidoEn { get; private set; }

    public DateTime? UltimaLecturaEn { get; private set; }
    public DateTime? UltimaCaidaEn { get; private set; }
    public int? Bateria { get; private set; }

    private EstadoSignosActual()
    {
    }

    public static EstadoSignosActual Iniciar(Guid hospitalizacionId, Guid pacienteId, string codigoDispositivo) =>
        new() { HospitalizacionId = hospitalizacionId, PacienteId = pacienteId, CodigoDispositivo = codigoDispositivo };

    public void Aplicar(LecturaSignos lectura)
    {
        ArgumentNullException.ThrowIfNull(lectura);
        var t = lectura.MedidoEn;

        (FcValor, FcMedidoEn) = Avanzar(FcValor, FcMedidoEn, lectura.Fc, t);
        (FrValor, FrMedidoEn) = Avanzar(FrValor, FrMedidoEn, lectura.Fr, t);
        (Spo2Valor, Spo2MedidoEn) = Avanzar(Spo2Valor, Spo2MedidoEn, lectura.Spo2, t);
        (TempValor, TempMedidoEn) = Avanzar(TempValor, TempMedidoEn, lectura.Temperatura, t);
        (PasValor, PasMedidoEn) = Avanzar(PasValor, PasMedidoEn, lectura.Pas, t);
        (PadValor, PadMedidoEn) = Avanzar(PadValor, PadMedidoEn, lectura.Pad, t);

        if (lectura.Caida && (UltimaCaidaEn is null || t > UltimaCaidaEn))
        {
            UltimaCaidaEn = t;
        }

        if (UltimaLecturaEn is null || t > UltimaLecturaEn)
        {
            UltimaLecturaEn = t;
            CodigoDispositivo = lectura.CodigoDispositivo;
            Bateria = lectura.Bateria ?? Bateria;
        }
    }

    private static (T? Valor, DateTime? MedidoEn) Avanzar<T>(T? actual, DateTime? actualEn, T? nuevo, DateTime nuevoEn)
        where T : struct =>
        nuevo is not null && (actualEn is null || nuevoEn > actualEn) ? (nuevo, nuevoEn) : (actual, actualEn);
}
