using System.Globalization;
using FluentValidation;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Application.Vinculacion;

/// <summary>
/// Paciente de la vinculación: uno existente (<see cref="PacienteId"/>) o los datos de uno nuevo, con las mismas reglas
/// del ingreso (si el documento ya existe, se reutiliza ese paciente).
/// </summary>
public sealed record PacienteVinculacion(
    Guid? PacienteId = null, string? NombreCompleto = null, string? TipoDocumento = null, string? NumeroDocumento = null,
    string? FechaNacimiento = null, int? Edad = null, bool? ConfusionBasal = null);

public sealed record ConfirmarVinculacionComando(
    string? TokenVinculacion,
    PacienteVinculacion? Paciente,
    Guid? CamaId,
    string? DiagnosticoIngreso,
    TomaManualComando? ObservacionInicial,
    Guid UsuarioId,
    string? Ip);

/// <summary>Resultado de la confirmación. <see cref="FalloObservacionInicial"/> es para el log del adaptador; no viaja al cliente.</summary>
public sealed record VinculacionConfirmada(VinculacionConfirmadaDto Respuesta, Exception? FalloObservacionInicial);

/// <summary>
/// Cierra la vinculación por QR: ingreso del paciente (si hace falta), vinculación del sensor, observación inicial y
/// primer NEWS2/MEWS. Hay dos bases (transaccional e historial), así que el orden evita dejar algo a medias:
/// <list type="number">
/// <item>Se valida todo y se construye la observación en el dominio: si algo es inválido, 400 y no se guarda nada.</item>
/// <item>En una transacción de la base transaccional: ingreso, vinculación y código marcado como usado.</item>
/// <item>Observación y evaluación en el historial. Si falla, el paciente ya quedó vinculado: se responde 201 con el
/// aviso de registrar la observación de nuevo.</item>
/// <item>Se invalida la caché del dispositivo y se publica <c>vinculado</c> con cama e iniciales.</item>
/// </list>
/// </summary>
public sealed class ConfirmarVinculacion(
    IValidator<RegistrarIngresoPacienteComando> validadorIngreso,
    TomaInicialValidador validadorToma,
    IRepositorioCodigosVinculacion codigos,
    IRepositorioDispositivos dispositivos,
    IRepositorioPacientes pacientes,
    RegistrarIngresoPaciente ingreso,
    VincularDispositivo vincular,
    ServicioObservaciones observaciones,
    ServicioCodigosVinculacion servicioCodigos,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IResolutorDispositivos resolutorDispositivos,
    IReloj reloj)
{
    /// <summary>Campos del ingreso que en este cuerpo van dentro de <c>paciente</c>.</summary>
    private static readonly HashSet<string> CamposDelPaciente = ["nombreCompleto", "tipoDocumento", "numeroDocumento", "fechaNacimiento", "edad"];

    public async Task<Resultado<VinculacionConfirmada>> EjecutarAsync(ConfirmarVinculacionComando comando, CancellationToken ct = default)
    {
        if (ValidarForma(comando) is { } errorForma)
        {
            return errorForma;
        }

        var ahora = reloj.AhoraUtc;
        var token = comando.TokenVinculacion!.Trim();
        var codigo = await codigos.ObtenerPorHashTokenAsync(CodigoVinculacion.Hash(token), ct);
        if (codigo is null || !codigo.TokenCoincide(token, ahora))
        {
            return ErroresVinculacion.VinculacionNoValida;
        }

        var dispositivo = (await dispositivos.ObtenerPorIdAsync(codigo.DispositivoId, ct))!;

        Paciente? elegido = null;
        if (comando.Paciente!.PacienteId is { } pacienteId)
        {
            elegido = await pacientes.ObtenerPorIdAsync(pacienteId, ct);
            if (elegido is null)
            {
                return ErroresPaciente.NoEncontrado;
            }
        }

        // 1. Validación completa: datos del ingreso (mismas reglas que POST /pacientes) y toma inicial.
        var comandoIngreso = ComandoIngreso(comando, elegido);
        var errores = new Dictionary<string, string[]>();
        if (await validadorIngreso.ValidarAsync(comandoIngreso, ct) is { } errorIngreso)
        {
            foreach (var (campo, mensajes) in errorIngreso.Detalles)
            {
                errores[CamposDelPaciente.Contains(campo) ? $"paciente.{campo}" : campo] = mensajes;
            }
        }

        if (await validadorToma.ValidarAsync(comando.ObservacionInicial!, ct) is { } errorToma)
        {
            foreach (var (campo, mensajes) in errorToma.Detalles)
            {
                errores[$"observacionInicial.{campo}"] = mensajes;
            }
        }

        if (errores.Count > 0)
        {
            return Error.Validacion(errores);
        }

        var preparado = await ingreso.PrepararAsync(comandoIngreso, elegido, ct);
        if (!preparado.EsExito)
        {
            return preparado.Error;
        }

        var (paciente, hospitalizacion, cama) = preparado.Valor;
        if (comando.Paciente.ConfusionBasal is { } confusionBasal)
        {
            paciente.CambiarConfusionBasal(confusionBasal, ahora);
        }

        ObservacionEnfermeria observacion;
        try
        {
            observacion = observaciones.Construir(hospitalizacion, comando.ObservacionInicial!, comando.UsuarioId, ahora);
        }
        catch (ExcepcionDeDominio ex)
        {
            return ErroresComunes.ValidacionDeCampo("observacionInicial", ex.Message);
        }

        // 2. Una transacción: ingreso, vinculación y código usado.
        codigo.MarcarUsado(ahora);
        var asignacion = await vincular.PrepararAsync(dispositivo, hospitalizacion, comando.UsuarioId, comando.Ip, AccionAuditoria.VinculacionPorQr, ct);
        if (!asignacion.EsExito)
        {
            return asignacion.Error;
        }

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        resolutorDispositivos.Invalidar(dispositivo.Codigo);

        // 3. Observación inicial y primer NEWS2/MEWS en el historial.
        EvaluacionRiesgoDto? riesgo = null;
        Exception? fallo = null;
        try
        {
            var evaluado = await observaciones.GuardarYEvaluarAsync(hospitalizacion, observacion, ct);
            if (evaluado.EsExito)
            {
                riesgo = evaluado.Valor;
                auditoria.Agregar(RegistroAuditoria.Registrar(
                    AccionAuditoria.ObservacionRegistrada, ahora, comando.UsuarioId, comando.Ip,
                    $"Observación {observacion.Id} de la hospitalización {hospitalizacion.Id} (inicial)."));
                await unidadDeTrabajo.GuardarCambiosAsync(ct);
            }
            else
            {
                fallo = new InvalidOperationException($"El historial respondió {evaluado.Error.Codigo}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            fallo = ex;
        }

        // 4. El wearable pasa a "vinculado" y empieza a enviar telemetría.
        await servicioCodigos.PublicarVinculadoAsync(dispositivo.Codigo, cama.Codigo, paciente.NombreCompleto, ct);

        var asignada = asignacion.Valor;
        var respuesta = new VinculacionConfirmadaDto(
            FichaPacienteDto.Desde(paciente, preparado.Valor.Lectura(dispositivo.Id, dispositivo.Codigo, asignada.AsignadoEn), DateOnly.FromDateTime(ahora)),
            new DispositivoVinculadoDto(dispositivo.Id, dispositivo.Codigo, asignada.AsignadoEn),
            riesgo,
            fallo is null ? [] : [AvisosClinicos.ObservacionInicialNoGuardada]);
        return new VinculacionConfirmada(respuesta, fallo);
    }

    private static Error? ValidarForma(ConfirmarVinculacionComando comando)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(comando.TokenVinculacion))
        {
            errores["tokenVinculacion"] = ["El token de vinculación es obligatorio: se obtiene al escanear el QR."];
        }

        if (comando.Paciente is null)
        {
            errores["paciente"] = ["Indica el paciente: pacienteId de uno existente o los datos de uno nuevo."];
        }

        if (comando.ObservacionInicial is null)
        {
            errores["observacionInicial"] = ["La observación inicial es obligatoria (oxígeno, conciencia y presión)."];
        }

        return errores.Count == 0 ? null : Error.Validacion(errores);
    }

    /// <summary>El comando del ingreso: con un paciente existente, sus datos guardados (solo cuentan cama y diagnóstico).</summary>
    private static RegistrarIngresoPacienteComando ComandoIngreso(ConfirmarVinculacionComando c, Paciente? elegido) =>
        elegido is null
            ? new RegistrarIngresoPacienteComando(
                c.Paciente!.NombreCompleto ?? string.Empty, c.Paciente.TipoDocumento ?? string.Empty, c.Paciente.NumeroDocumento ?? string.Empty,
                c.Paciente.FechaNacimiento, c.Paciente.Edad, c.CamaId, c.DiagnosticoIngreso ?? string.Empty, c.UsuarioId, c.Ip)
            : new RegistrarIngresoPacienteComando(
                elegido.NombreCompleto, elegido.TipoDocumento.ToString(), elegido.NumeroDocumento,
                elegido.FechaNacimiento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, c.CamaId,
                c.DiagnosticoIngreso ?? string.Empty, c.UsuarioId, c.Ip);
}
