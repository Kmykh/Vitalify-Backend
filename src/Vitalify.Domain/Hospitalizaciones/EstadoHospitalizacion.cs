namespace Vitalify.Domain.Hospitalizaciones;

public enum EstadoHospitalizacion
{
    Activa = 1,
    Finalizada = 2,
}

public enum MotivoEgreso
{
    AltaMedica = 1,
    Traslado = 2,
    Fallecimiento = 3,
    Voluntaria = 4,
    Otro = 5,
}

public enum MotivoLiberacion
{
    /// <summary>La enfermera desvinculó el sensor.</summary>
    Manual = 1,

    /// <summary>Se liberó automáticamente al registrar el egreso.</summary>
    Egreso = 2,
}
