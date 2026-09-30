using Vitalify.Application.Comun;

namespace Vitalify.Application.Pacientes;

public static class ErroresPaciente
{
    public static readonly Error NoEncontrado = Error.NoEncontrado("paciente-no-encontrado", "El paciente no existe.");

    public static readonly Error YaHospitalizado =
        Error.Conflicto("paciente-ya-hospitalizado", "El paciente ya tiene una hospitalización activa.");

    /// <summary>Dos ingresos simultáneos del mismo paciente nuevo: el segundo choca con el índice del documento.</summary>
    public static readonly Error DocumentoDuplicado =
        Error.Conflicto("paciente-documento-duplicado", "Otro usuario registró a este paciente al mismo tiempo. Intenta de nuevo.");

    /// <summary>Para vincular un sensor o registrar el egreso (409).</summary>
    public static readonly Error SinHospitalizacionActiva =
        Error.Conflicto("sin-hospitalizacion-activa", "El paciente no tiene una hospitalización activa.");

    /// <summary>Para editar los datos o liberar el sensor (404).</summary>
    public static readonly Error SinHospitalizacionActivaNoEncontrada =
        Error.NoEncontrado("sin-hospitalizacion-activa", "El paciente no tiene una hospitalización activa.");

    public static readonly Error YaTieneDispositivo =
        Error.Conflicto("paciente-ya-tiene-dispositivo", "El paciente ya tiene un sensor vinculado. Libéralo antes de vincular otro.");

    public static readonly Error SinDispositivo =
        Error.NoEncontrado("sin-dispositivo-vinculado", "El paciente no tiene un sensor vinculado.");
}
