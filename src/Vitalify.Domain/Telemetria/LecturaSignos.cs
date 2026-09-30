using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Telemetria;

/// <summary>
/// Una lectura aceptada en el historial. Se identifica por <c>(CodigoDispositivo, MedidoEn)</c>, lo que la hace
/// idempotente ante los reenvíos de MQTT (QoS 1). Guarda los ids del paciente y la hospitalización como valores:
/// el historial vive en otro esquema (y en la fase 7 en otra base) y no tiene claves foráneas.
/// </summary>
public sealed class LecturaSignos
{
    public string CodigoDispositivo { get; private set; } = null!;
    public DateTime MedidoEn { get; private set; }
    public DateTime RecibidoEn { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }
    public int? Fc { get; private set; }
    public int? Fr { get; private set; }
    public int? Spo2 { get; private set; }
    public decimal? Temperatura { get; private set; }
    public int? Pas { get; private set; }
    public int? Pad { get; private set; }
    public bool Caida { get; private set; }
    public int? Bateria { get; private set; }
    public long Seq { get; private set; }
    public OrigenLectura Origen { get; private set; }

    private LecturaSignos()
    {
    }

    public static LecturaSignos Registrar(
        string codigoDispositivo, DateTime medidoEn, DateTime recibidoEn, Guid hospitalizacionId, Guid pacienteId,
        SignosValidos signos, bool caida, long seq, OrigenLectura origen)
    {
        ArgumentNullException.ThrowIfNull(signos);
        if (!signos.TieneAlgunSigno && !caida)
        {
            throw new ExcepcionDeDominio("Una lectura necesita al menos un signo válido o un evento de caída.");
        }

        return new LecturaSignos
        {
            CodigoDispositivo = codigoDispositivo,
            MedidoEn = medidoEn,
            RecibidoEn = recibidoEn,
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            Fc = signos.Fc,
            Fr = signos.Fr,
            Spo2 = signos.Spo2,
            Temperatura = signos.Temperatura,
            Pas = signos.Pas,
            Pad = signos.Pad,
            Caida = caida,
            Bateria = signos.Bateria,
            Seq = seq,
            Origen = origen,
        };
    }
}
