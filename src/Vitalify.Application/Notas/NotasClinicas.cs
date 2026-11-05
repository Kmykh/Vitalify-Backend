using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Historial;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Notas;

namespace Vitalify.Application.Notas;

public static class ErroresNota
{
    public static readonly Error NoEncontrada = Error.NoEncontrado("nota-no-encontrada", "La nota no existe.");

    public static readonly Error DeOtroAutor = Error.Prohibido("nota-de-otro-autor", "Solo el autor puede corregir la nota.");

    public static readonly Error FueraDePlazo = Error.Conflicto(
        "nota-fuera-de-plazo", $"La nota solo se puede corregir dentro de las {NotaClinica.PlazoEdicion.TotalHours:0} h siguientes a su registro.");
}

/// <summary>Una nota clínica con su autor.</summary>
/// <param name="Version">Versión vigente: 1 si nunca se corrigió.</param>
/// <param name="EditableHasta">Hasta cuándo puede corregirla su autor (24 h desde que la escribió).</param>
public sealed record NotaDto(
    Guid Id, Guid PacienteId, Guid HospitalizacionId, string Tipo, string Texto, ProfesionalDto Autor, DateTime CreadaEn,
    DateTime? EditadaEn, int Version, DateTime EditableHasta)
{
    public static NotaDto Desde(NotaClinica n, ProfesionalDto autor) =>
        new(n.Id, n.PacienteId, n.HospitalizacionId, n.Tipo.ToString(), n.Texto, autor, n.CreadaEn, n.EditadaEn, n.Version,
            n.CreadaEn + NotaClinica.PlazoEdicion);
}

public sealed record NotasPacienteDto(Guid PacienteId, Guid HospitalizacionId, IReadOnlyList<NotaDto> Notas);

/// <summary>Texto que tuvo la nota antes de una corrección.</summary>
public sealed record NotaVersionDto(int Numero, string Tipo, string Texto, DateTime VigenteDesde, DateTime ReemplazadaEn);

public sealed record VersionesNotaDto(Guid NotaId, Guid PacienteId, int VersionActual, IReadOnlyList<NotaVersionDto> Versiones);

/// <summary>Arma las notas con el nombre y el rol de su autor.</summary>
public sealed class PresentadorNotas(DirectorioProfesionales directorio)
{
    public async Task<IReadOnlyList<NotaDto>> PresentarAsync(IReadOnlyList<NotaClinica> notas, CancellationToken ct = default)
    {
        var autores = await directorio.ObtenerAsync(notas.Select(n => n.AutorId), ct);
        return notas.Select(n => NotaDto.Desde(n, DirectorioProfesionales.De(autores, n.AutorId))).ToList();
    }
}

internal static class ReglasNota
{
    public static void Tipo<T>(IRuleBuilder<T, string?> regla, bool obligatorio) =>
        regla.Must(t => (!obligatorio && t is null) || Enumeraciones.TryParseNombre<TipoNota>(t, out _))
            .WithMessage($"El tipo debe ser {Enumeraciones.Nombres<TipoNota>()}.");

    public static void Texto<T>(IRuleBuilder<T, string?> regla) =>
        regla.Must(t => t?.Trim().Length is >= NotaClinica.LargoMinimoTexto and <= NotaClinica.LargoMaximoTexto)
            .WithMessage($"El texto es obligatorio y debe tener entre {NotaClinica.LargoMinimoTexto} y {NotaClinica.LargoMaximoTexto} caracteres.");
}

/// <param name="Tipo"><c>Evolucion</c>, <c>Enfermeria</c> o <c>Interconsulta</c>.</param>
public sealed record RegistrarNotaComando(Guid PacienteId, string? Tipo, string? Texto, Guid UsuarioId, string? Ip);

public sealed class RegistrarNotaValidador : AbstractValidator<RegistrarNotaComando>
{
    public RegistrarNotaValidador()
    {
        ReglasNota.Tipo(RuleFor(c => c.Tipo), obligatorio: true);
        ReglasNota.Texto(RuleFor(c => c.Texto));
    }
}

/// <summary>Registra una nota en la hospitalización activa del paciente (HU22 E1): queda con fecha, hora y autor.</summary>
public sealed class RegistrarNota(
    IValidator<RegistrarNotaComando> validador,
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioNotas notas,
    PresentadorNotas presentador,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IReloj reloj)
{
    public async Task<Resultado<NotaDto>> EjecutarAsync(RegistrarNotaComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        if (await pacientes.ObtenerPorIdAsync(comando.PacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(comando.PacienteId, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActiva;
        }

        var ahora = reloj.AhoraUtc;
        Enumeraciones.TryParseNombre<TipoNota>(comando.Tipo, out var tipo);
        NotaClinica nota;
        try
        {
            nota = NotaClinica.Registrar(hospitalizacion.Id, comando.PacienteId, comando.UsuarioId, tipo, comando.Texto!, ahora);
        }
        catch (ExcepcionDeDominio ex)
        {
            return ErroresComunes.ValidacionDeCampo("texto", ex.Message);
        }

        notas.Agregar(nota);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.NotaRegistrada, ahora, comando.UsuarioId, comando.Ip, $"Nota {nota.Id} de la hospitalización {hospitalizacion.Id}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? (await presentador.PresentarAsync([nota], ct))[0] : guardado.Error;
    }
}

public sealed record ListarNotasConsulta(Guid PacienteId, Guid? HospitalizacionId, Guid UsuarioId, string? Ip);

/// <summary>Notas del episodio (por defecto el activo o el último), la más reciente primero.</summary>
public sealed class ListarNotas(EpisodiosPaciente episodios, IRepositorioNotas notas, PresentadorNotas presentador, AuditoriaHistorial auditoria)
{
    public async Task<Resultado<NotasPacienteDto>> EjecutarAsync(ListarNotasConsulta consulta, CancellationToken ct = default)
    {
        var elegido = await episodios.ElegirAsync(consulta.PacienteId, consulta.HospitalizacionId, ct);
        if (!elegido.EsExito)
        {
            return elegido.Error;
        }

        var hospitalizacionId = elegido.Valor.Hospitalizacion.Id;
        var lista = await presentador.PresentarAsync(await notas.ListarDeHospitalizacionAsync(hospitalizacionId, null, ct), ct);
        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, hospitalizacionId, "notas", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito ? new NotasPacienteDto(consulta.PacienteId, hospitalizacionId, lista) : auditado.Error;
    }
}

/// <param name="Tipo">Opcional: si falta, se mantiene el tipo actual.</param>
public sealed record EditarNotaComando(Guid NotaId, string? Tipo, string? Texto, Guid UsuarioId, string? Ip);

public sealed class EditarNotaValidador : AbstractValidator<EditarNotaComando>
{
    public EditarNotaValidador()
    {
        ReglasNota.Tipo(RuleFor(c => c.Tipo), obligatorio: false);
        ReglasNota.Texto(RuleFor(c => c.Texto));
    }
}

/// <summary>
/// Corrige una nota (HU22 E2): solo su autor y dentro de las 24 h. El texto anterior queda en
/// <c>nota_clinica_version</c> y la nota guarda la fecha de la edición; nada se borra (NTS 139).
/// </summary>
public sealed class EditarNota(
    IValidator<EditarNotaComando> validador,
    IRepositorioNotas notas,
    PresentadorNotas presentador,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IReloj reloj)
{
    public async Task<Resultado<NotaDto>> EjecutarAsync(EditarNotaComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var nota = await notas.ObtenerPorIdAsync(comando.NotaId, ct);
        if (nota is null)
        {
            return ErroresNota.NoEncontrada;
        }

        var ahora = reloj.AhoraUtc;
        switch (nota.PorQueNoEditable(comando.UsuarioId, ahora))
        {
            case MotivoNoEditable.OtroAutor:
                return ErroresNota.DeOtroAutor;
            case MotivoNoEditable.FueraDePlazo:
                return ErroresNota.FueraDePlazo;
        }

        var tipo = Enumeraciones.TryParseNombre<TipoNota>(comando.Tipo, out var nuevoTipo) ? nuevoTipo : nota.Tipo;
        var anterior = nota.Editar(comando.UsuarioId, tipo, comando.Texto!, ahora);
        if (anterior is not null)
        {
            notas.AgregarVersion(anterior);
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.NotaEditada, ahora, comando.UsuarioId, comando.Ip, $"Nota {nota.Id}: versión {nota.Version}."));
            var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
            if (!guardado.EsExito)
            {
                return guardado.Error;
            }
        }

        return (await presentador.PresentarAsync([nota], ct))[0];
    }
}

public sealed record ListarVersionesNotaConsulta(Guid NotaId, Guid UsuarioId, string? Ip);

/// <summary>Versiones anteriores de una nota, de la más reciente a la original. Se audita como consulta del historial.</summary>
public sealed class ListarVersionesNota(IRepositorioNotas notas, AuditoriaHistorial auditoria)
{
    public async Task<Resultado<VersionesNotaDto>> EjecutarAsync(ListarVersionesNotaConsulta consulta, CancellationToken ct = default)
    {
        var nota = await notas.ObtenerPorIdAsync(consulta.NotaId, ct);
        if (nota is null)
        {
            return ErroresNota.NoEncontrada;
        }

        var versiones = await notas.ListarVersionesAsync(nota.Id, ct);
        var auditado = await auditoria.RegistrarAsync(nota.PacienteId, nota.HospitalizacionId, $"notas/{nota.Id}/versiones", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito
            ? new VersionesNotaDto(nota.Id, nota.PacienteId, nota.Version,
                versiones.Select(v => new NotaVersionDto(v.Numero, v.Tipo.ToString(), v.Texto, v.VigenteDesde, v.ReemplazadaEn)).ToList())
            : auditado.Error;
    }
}
