using System.Globalization;
using FluentValidation;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Alertas;

/// <summary>Usuario clínico con su servicio: las alertas solo se ven y se atienden dentro del propio servicio.</summary>
public sealed class AccesoServicio(IRepositorioUsuarios usuarios)
{
    public async Task<Resultado<Usuario>> UsuarioConServicioAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, ct);
        return usuario is { ServicioId: not null } ? usuario : ErroresAlerta.SinServicio;
    }
}

/// <summary>
/// Lo que vigila el worker cada 30 s: wearables sin señal (180 s sin lecturas o <c>offline</c>), tomas manuales
/// vencidas, alertas que hay que escalar y alertas abiertas que hay que avisar a la enfermera que entra de turno.
/// </summary>
public sealed class VigilarAlertas(
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioEstadoSignos estados,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioAlertas alertas,
    IRepositorioServicios servicios,
    IRepositorioProgramacion programacion,
    IPresenciaDispositivos presencia,
    ServicioAlertas servicioAlertas,
    NotificadorAlertas notificador,
    ContextoHospitalizaciones contextos,
    CatalogoTurnos catalogo,
    OpcionesAlertas opciones,
    OpcionesClinicas opcionesClinicas,
    IReloj reloj)
{
    public sealed record Resumen(int SinSenal, int TomasPendientes, int Escaladas, int AvisosCambioDeTurno);

    /// <param name="ultimaRevision">Momento de la vuelta anterior, para detectar los turnos que empezaron desde entonces.</param>
    public async Task<Resumen> EjecutarAsync(DateTime? ultimaRevision = null, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var sinSenal = await VigilarSenalAsync(ahora, ct);
        var tomas = await VigilarTomasAsync(ahora, ct);

        var escaladas = 0;
        foreach (var alerta in (await alertas.ListarAbiertasAsync(null, ct)).Where(a => a.DebeEscalar(ahora, opciones.Escalamiento)))
        {
            await servicioAlertas.EscalarAsync(alerta, ct);
            escaladas++;
        }

        var avisos = ultimaRevision is { } desde ? await AvisarCambioDeTurnoAsync(desde, ahora, ct) : 0;
        return new Resumen(sinSenal, tomas, escaladas, avisos);
    }

    private async Task<int> VigilarSenalAsync(DateTime ahora, CancellationToken ct)
    {
        var conSensor = await hospitalizaciones.ListarActivasConDispositivoAsync(ct);
        var estadosSignos = await estados.ObtenerVariosAsync(conSensor.Select(h => h.HospitalizacionId).ToList(), ct);
        var sinSenal = 0;
        foreach (var h in conSensor)
        {
            var ultimaLectura = estadosSignos.GetValueOrDefault(h.HospitalizacionId)?.UltimaLecturaEn;
            var desconectado = presencia.Obtener(h.CodigoDispositivo!) is { EnLinea: false };
            var referencia = ultimaLectura ?? h.DispositivoAsignadoEn!.Value;
            if (!desconectado && ahora - referencia < opciones.SinSenal)
            {
                continue;
            }

            await servicioAlertas.DispararAsync(new DisparoAlerta(
                h.HospitalizacionId, TipoAlerta.SinSenal, PrioridadAlerta.Media,
                Detalle: new Dictionary<string, string>
                {
                    ["dispositivo"] = h.CodigoDispositivo!,
                    ["motivo"] = desconectado ? "offline" : "sin-lecturas",
                    ["ultimaLecturaEn"] = ultimaLectura?.ToString("O", CultureInfo.InvariantCulture) ?? "nunca",
                }), ct);
            sinSenal++;
        }

        return sinSenal;
    }

    private async Task<int> VigilarTomasAsync(DateTime ahora, CancellationToken ct)
    {
        var activas = await hospitalizaciones.ListarActivasDeServicioAsync(null, ct);
        var ultimas = await evaluaciones.ObtenerUltimasAsync(activas.Select(h => h.HospitalizacionId).ToList(), ct);
        var vencidas = 0;
        foreach (var (hospitalizacionId, evaluacion) in ultimas)
        {
            if (evaluacion.ProximaTomaManualEn is not { } proxima || ahora <= proxima + opcionesClinicas.ToleranciaToma)
            {
                continue;
            }

            await servicioAlertas.DispararAsync(new DisparoAlerta(
                hospitalizacionId, TipoAlerta.TomaPendiente, PrioridadAlerta.Baja,
                Detalle: new Dictionary<string, string> { ["proximaTomaManualEn"] = proxima.ToString("O", CultureInfo.InvariantCulture) }), ct);
            vencidas++;
        }

        return vencidas;
    }

    /// <summary>Al empezar un turno, las alertas abiertas del servicio se avisan a las enfermeras que entran.</summary>
    private async Task<int> AvisarCambioDeTurnoAsync(DateTime desde, DateTime ahora, CancellationToken ct)
    {
        var turnos = catalogo.TurnosQueEmpiezanEntre(desde, ahora);
        if (turnos.Count == 0)
        {
            return 0;
        }

        var avisos = 0;
        foreach (var servicio in (await servicios.ListarAsync(ct)).Where(s => s.Activo))
        {
            var programados = (await programacion.ListarAsync(servicio.Id, turnos.Min(t => t.Fecha), turnos.Max(t => t.Fecha), ct))
                .Where(p => turnos.Contains(p.Turno))
                .Select(p => p.UsuarioId)
                .Distinct()
                .ToList();
            if (programados.Count == 0)
            {
                continue;
            }

            foreach (var alerta in await alertas.ListarAbiertasAsync(servicio.Id, ct))
            {
                if (await contextos.ObtenerAsync(alerta.HospitalizacionId, ct) is { } contexto)
                {
                    await notificador.NotificarAsync(alerta, contexto.CodigoCama, MotivosNotificacion.CambioDeTurno, programados, ct);
                    avisos++;
                }
            }
        }

        return avisos;
    }
}

public sealed record TomarAlertaComando(Guid AlertaId, Guid UsuarioId, string? Ip);

/// <summary>Una enfermera o un médico del servicio toma la alerta: pasa a en atención.</summary>
public sealed class TomarAlerta(
    IRepositorioAlertas alertas, AccesoServicio acceso, ContextoHospitalizaciones contextos, NotificadorAlertas notificador,
    IRepositorioAuditoria auditoria, IUnidadDeTrabajo unidadDeTrabajo, IReloj reloj)
{
    public async Task<Resultado<AlertaDto>> EjecutarAsync(TomarAlertaComando comando, CancellationToken ct = default)
    {
        var alerta = await alertas.ObtenerPorIdAsync(comando.AlertaId, ct);
        if (alerta is null)
        {
            return ErroresAlerta.NoEncontrada;
        }

        var usuario = await acceso.UsuarioConServicioAsync(comando.UsuarioId, ct);
        if (!usuario.EsExito)
        {
            return usuario.Error;
        }

        if (usuario.Valor.ServicioId != alerta.ServicioId)
        {
            return ErroresAlerta.OtroServicio;
        }

        if (alerta.Estado == EstadoAlerta.Atendida)
        {
            return ErroresAlerta.YaAtendida;
        }

        if (alerta.Estado == EstadoAlerta.EnAtencion && alerta.TomadaPor != comando.UsuarioId)
        {
            return ErroresAlerta.TomadaPorOtro;
        }

        var ahora = reloj.AhoraUtc;
        alerta.Tomar(comando.UsuarioId, ahora);
        auditoria.Agregar(RegistroAuditoria.Registrar(AccionAuditoria.AlertaTomada, ahora, comando.UsuarioId, comando.Ip, $"Alerta {alerta.Id}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        var cama = (await contextos.ObtenerAsync(alerta.HospitalizacionId, ct))?.CodigoCama ?? string.Empty;
        await notificador.SincronizarAsync(alerta, cama, ct);
        return AlertaDto.Desde(new AlertaConCama(alerta, cama));
    }
}

public sealed record AtenderAlertaComando(Guid AlertaId, string? Nota, Guid UsuarioId, string? Ip);

public sealed class AtenderAlertaValidador : AbstractValidator<AtenderAlertaComando>
{
    public AtenderAlertaValidador()
    {
        RuleFor(c => c.Nota)
            .Must(n => n?.Trim().Length is >= AlertaClinica.LargoMinimoNota and <= AlertaClinica.LargoMaximoNota)
            .WithMessage($"La nota de atención es obligatoria y debe tener entre {AlertaClinica.LargoMinimoNota} y {AlertaClinica.LargoMaximoNota} caracteres.");
    }
}

/// <summary>Cierra la alerta con una nota obligatoria (qué se hizo).</summary>
public sealed class AtenderAlerta(
    IValidator<AtenderAlertaComando> validador, IRepositorioAlertas alertas, AccesoServicio acceso, ContextoHospitalizaciones contextos,
    NotificadorAlertas notificador, IRepositorioAuditoria auditoria, IUnidadDeTrabajo unidadDeTrabajo, IReloj reloj)
{
    public async Task<Resultado<AlertaDto>> EjecutarAsync(AtenderAlertaComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var alerta = await alertas.ObtenerPorIdAsync(comando.AlertaId, ct);
        if (alerta is null)
        {
            return ErroresAlerta.NoEncontrada;
        }

        var usuario = await acceso.UsuarioConServicioAsync(comando.UsuarioId, ct);
        if (!usuario.EsExito)
        {
            return usuario.Error;
        }

        if (usuario.Valor.ServicioId != alerta.ServicioId)
        {
            return ErroresAlerta.OtroServicio;
        }

        if (!alerta.EstaAbierta)
        {
            return ErroresAlerta.YaAtendida;
        }

        var ahora = reloj.AhoraUtc;
        alerta.Atender(comando.UsuarioId, comando.Nota!, ahora);
        auditoria.Agregar(RegistroAuditoria.Registrar(AccionAuditoria.AlertaAtendida, ahora, comando.UsuarioId, comando.Ip, $"Alerta {alerta.Id}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        var cama = (await contextos.ObtenerAsync(alerta.HospitalizacionId, ct))?.CodigoCama ?? string.Empty;
        await notificador.SincronizarAsync(alerta, cama, ct);
        return AlertaDto.Desde(new AlertaConCama(alerta, cama));
    }
}

/// <param name="Estado"><c>pendiente</c>, <c>en-atencion</c>, <c>atendida</c> o <c>abiertas</c> (por defecto).</param>
public sealed record ListarAlertasConsulta(
    Guid UsuarioId, string? Estado = null, Guid? ServicioId = null, string? Tipo = null, DateTime? Desde = null, DateTime? Hasta = null,
    int Pagina = 1, int Tamano = 20);

public sealed class ListarAlertasValidador : AbstractValidator<ListarAlertasConsulta>
{
    public ListarAlertasValidador()
    {
        RuleFor(c => c.Estado).Must(e => string.IsNullOrWhiteSpace(e) || e.Trim() == "abiertas" || TextosAlerta.TryEstado(e, out _))
            .WithMessage("estado debe ser pendiente, en-atencion, atendida o abiertas.");
        RuleFor(c => c.Tipo).Must(t => string.IsNullOrWhiteSpace(t) || TextosAlerta.TryTipo(t, out _))
            .WithMessage("tipo debe ser riesgo, caida, sin-senal, toma-pendiente o preocupacion-clinica.");
        RuleFor(c => c.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página debe ser 1 o mayor.");
        RuleFor(c => c.Tamano).InclusiveBetween(1, 100).WithMessage("El tamaño de página debe estar entre 1 y 100.");
        RuleFor(c => c).Must(c => c.Desde is null || c.Hasta is null || c.Desde <= c.Hasta).OverridePropertyName("desde")
            .WithMessage("desde no puede ser posterior a hasta.");
    }
}

/// <summary>Alertas del servicio del usuario, por defecto las abiertas, ordenadas por prioridad y antigüedad.</summary>
public sealed class ListarAlertas(IValidator<ListarAlertasConsulta> validador, IRepositorioAlertas alertas, AccesoServicio acceso)
{
    public async Task<Resultado<Pagina<AlertaDto>>> EjecutarAsync(ListarAlertasConsulta consulta, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(consulta, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var usuario = await acceso.UsuarioConServicioAsync(consulta.UsuarioId, ct);
        if (!usuario.EsExito)
        {
            return usuario.Error;
        }

        var servicioId = consulta.ServicioId ?? usuario.Valor.ServicioId!.Value;
        if (servicioId != usuario.Valor.ServicioId)
        {
            return ErroresAlerta.OtroServicio;
        }

        IReadOnlyCollection<EstadoAlerta> estados = string.IsNullOrWhiteSpace(consulta.Estado) || consulta.Estado.Trim() == "abiertas"
            ? [EstadoAlerta.Pendiente, EstadoAlerta.EnAtencion]
            : TextosAlerta.TryEstado(consulta.Estado, out var estado) ? [estado] : [];
        TipoAlerta? tipo = TextosAlerta.TryTipo(consulta.Tipo, out var t) ? t : null;

        var pagina = await alertas.ListarAsync(
            new FiltroAlertas(estados, servicioId, tipo, consulta.Desde?.ToUniversalTime(), consulta.Hasta?.ToUniversalTime()),
            consulta.Pagina, consulta.Tamano, ct);
        return pagina.Mapear(AlertaDto.Desde);
    }
}

/// <summary>Historial de alertas del paciente en el servicio del usuario, con los tiempos de respuesta.</summary>
public sealed class ListarAlertasPaciente(IRepositorioPacientes pacientes, IRepositorioAlertas alertas, AccesoServicio acceso)
{
    public async Task<Resultado<IReadOnlyList<AlertaDto>>> EjecutarAsync(Guid pacienteId, Guid usuarioId, CancellationToken ct = default)
    {
        if (await pacientes.ObtenerPorIdAsync(pacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var usuario = await acceso.UsuarioConServicioAsync(usuarioId, ct);
        if (!usuario.EsExito)
        {
            return usuario.Error;
        }

        var filas = await alertas.ListarDePacienteAsync(pacienteId, usuario.Valor.ServicioId!.Value, ct);
        return Resultado<IReadOnlyList<AlertaDto>>.Exito(filas.Select(AlertaDto.Desde).ToList());
    }
}
