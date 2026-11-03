namespace Vitalify.Domain.Telemetria;

/// <summary>
/// Cinco minutos de lecturas del wearable de un episodio ya cerrado, que reemplazan a las lecturas crudas cuando el
/// egreso cumple los días de retención (<see cref="Vitalify.Domain.Historial.PoliticaRetencion"/>). Por variable guarda el promedio, el
/// mínimo, el máximo y cuántas mediciones válidas hubo: con eso se reagrupa a 15 min o 1 h con el promedio ponderado.
/// Lo escribe la base en la misma sentencia que borra las crudas; el código solo lo lee.
/// </summary>
public sealed class LecturaSignosResumen
{
    public Guid HospitalizacionId { get; private set; }

    /// <summary>Inicio del grupo de 5 minutos (alineado a la época UNIX).</summary>
    public DateTime Inicio { get; private set; }

    public Guid PacienteId { get; private set; }

    /// <summary>Lecturas crudas que resume.</summary>
    public int Lecturas { get; private set; }

    public bool Caida { get; private set; }

    public decimal? FcPromedio { get; private set; }
    public int? FcMinimo { get; private set; }
    public int? FcMaximo { get; private set; }
    public int FcMediciones { get; private set; }

    public decimal? FrPromedio { get; private set; }
    public int? FrMinimo { get; private set; }
    public int? FrMaximo { get; private set; }
    public int FrMediciones { get; private set; }

    public decimal? Spo2Promedio { get; private set; }
    public int? Spo2Minimo { get; private set; }
    public int? Spo2Maximo { get; private set; }
    public int Spo2Mediciones { get; private set; }

    public decimal? TemperaturaPromedio { get; private set; }
    public decimal? TemperaturaMinimo { get; private set; }
    public decimal? TemperaturaMaximo { get; private set; }
    public int TemperaturaMediciones { get; private set; }

    public decimal? PasPromedio { get; private set; }
    public int? PasMinimo { get; private set; }
    public int? PasMaximo { get; private set; }
    public int PasMediciones { get; private set; }

    public decimal? PadPromedio { get; private set; }
    public int? PadMinimo { get; private set; }
    public int? PadMaximo { get; private set; }
    public int PadMediciones { get; private set; }

    /// <summary>Cuándo se resumió (y se borraron las crudas).</summary>
    public DateTime ResumidoEn { get; private set; }

    private LecturaSignosResumen()
    {
    }
}
