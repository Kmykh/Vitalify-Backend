using FluentValidation;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Telemetria;

/// <summary>Textos estables de los estados calculados (los usa el cliente).</summary>
public static class TextosEstado
{
    public static string De(EstadoVariable estado) => estado switch
    {
        EstadoVariable.Vigente => "vigente",
        EstadoVariable.PendienteActualizacion => "pendiente-actualizacion",
        _ => "sin-datos",
    };

    public static string De(EstadoSenal senal) => senal switch
    {
        EstadoSenal.ConDatos => "con-datos",
        EstadoSenal.SinSenal => "sin-senal",
        _ => "sin-datos",
    };
}

/// <summary>Último valor válido de una variable, cuándo se midió y si sigue vigente.</summary>
public sealed record VariableSignoDto(decimal? Valor, DateTime? MedidoEn, string Estado);

public sealed record SignosActualesDto(
    Guid PacienteId,
    Guid HospitalizacionId,
    string? CodigoDispositivo,
    VariableSignoDto Fc,
    VariableSignoDto Fr,
    VariableSignoDto Spo2,
    VariableSignoDto Temperatura,
    VariableSignoDto Pas,
    VariableSignoDto Pad,
    DateTime? UltimaCaidaEn,
    int? Bateria,
    DateTime? UltimaLecturaEn,
    string Senal,
    string ConexionSensor);

/// <summary>
/// HU11: estado actual de los signos del paciente. El estado de cada variable se calcula al leer con
/// <see cref="IReloj"/>: si pasó la vigencia, se mantiene el último valor válido marcado como pendiente.
/// </summary>
public sealed class ObtenerSignosActuales(
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioEstadoSignos estados,
    IPresenciaDispositivos presencia,
    OpcionesTelemetria opciones,
    IReloj reloj)
{
    public async Task<Resultado<SignosActualesDto>> EjecutarAsync(Guid pacienteId, CancellationToken ct = default)
    {
        if (await pacientes.ObtenerPorIdAsync(pacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var activa = await hospitalizaciones.ObtenerLecturaActivaAsync(pacienteId, ct);
        if (activa is null)
        {
            return ErroresPaciente.SinHospitalizacionActivaNoEncontrada;
        }

        var ahora = reloj.AhoraUtc;
        var e = await estados.ObtenerAsync(activa.HospitalizacionId, ct);

        VariableSignoDto Variable(decimal? valor, DateTime? medidoEn) =>
            new(valor, medidoEn, TextosEstado.De(Vigencia.EstadoDe(medidoEn, ahora, opciones.Vigencia)));

        return new SignosActualesDto(
            pacienteId,
            activa.HospitalizacionId,
            activa.CodigoDispositivo,
            Variable(e?.FcValor, e?.FcMedidoEn),
            Variable(e?.FrValor, e?.FrMedidoEn),
            Variable(e?.Spo2Valor, e?.Spo2MedidoEn),
            Variable(e?.TempValor, e?.TempMedidoEn),
            Variable(e?.PasValor, e?.PasMedidoEn),
            Variable(e?.PadValor, e?.PadMedidoEn),
            e?.UltimaCaidaEn,
            e?.Bateria,
            e?.UltimaLecturaEn,
            TextosEstado.De(Vigencia.SenalDe(e?.UltimaLecturaEn, ahora, opciones.Vigencia)),
            RegistrarPresenciaDispositivo.Texto(activa.CodigoDispositivo is { } codigo ? presencia.Obtener(codigo) : null));
    }
}

public sealed record ListarIncidenciasConsulta(
    DateTime? Desde = null, DateTime? Hasta = null, string? Dispositivo = null, string? Tipo = null, int Pagina = 1, int Tamano = 20);

public sealed class ListarIncidenciasValidador : AbstractValidator<ListarIncidenciasConsulta>
{
    public const int TamanoMaximo = 100;

    public ListarIncidenciasValidador()
    {
        RuleFor(c => c.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página debe ser 1 o mayor.");
        RuleFor(c => c.Tamano).InclusiveBetween(1, TamanoMaximo).WithMessage($"El tamaño de página debe estar entre 1 y {TamanoMaximo}.");
        RuleFor(c => c.Tipo)
            .Must(t => t is null || Enumeraciones.TryParseNombre<TipoIncidencia>(t, out _))
            .WithMessage($"El tipo debe ser uno de: {Enumeraciones.Nombres<TipoIncidencia>()}.");
        RuleFor(c => c.Hasta)
            .Must((c, hasta) => c.Desde is null || hasta is null || c.Desde <= hasta)
            .WithMessage("hasta debe ser posterior a desde.");
    }
}

/// <summary>Log de incidencias de telemetría para el administrador: solo datos técnicos, nunca datos del paciente.</summary>
public sealed class ListarIncidencias(IValidator<ListarIncidenciasConsulta> validador, IRepositorioIncidencias incidencias)
{
    public async Task<Resultado<Pagina<IncidenciaDto>>> EjecutarAsync(ListarIncidenciasConsulta consulta, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(consulta, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        TipoIncidencia? tipo = Enumeraciones.TryParseNombre<TipoIncidencia>(consulta.Tipo, out var t) ? t : null;
        var filtro = new FiltroIncidencias(
            consulta.Desde?.ToUniversalTime(), consulta.Hasta?.ToUniversalTime(),
            string.IsNullOrWhiteSpace(consulta.Dispositivo) ? null : consulta.Dispositivo.Trim().ToUpperInvariant(), tipo);

        var pagina = await incidencias.ListarAsync(filtro, consulta.Pagina, consulta.Tamano, ct);
        return pagina.Mapear(IncidenciaDto.Desde);
    }
}

/// <summary>Códigos de los dispositivos vinculados a una hospitalización activa (lo usa el simulador).</summary>
public interface IListarDispositivosVinculados
{
    Task<IReadOnlyList<string>> EjecutarAsync(CancellationToken ct = default);
}

public sealed class ListarDispositivosVinculados(IRepositorioHospitalizaciones hospitalizaciones) : IListarDispositivosVinculados
{
    public Task<IReadOnlyList<string>> EjecutarAsync(CancellationToken ct = default) =>
        hospitalizaciones.ListarCodigosDispositivosVinculadosAsync(ct);
}
