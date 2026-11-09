using System.Globalization;
using System.Text;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Nnn;

namespace Vitalify.Application.Nnn;

/// <param name="Tipo"><c>nanda</c>, <c>nic</c> o <c>noc</c>.</param>
/// <param name="Q">Texto a buscar en el código o la etiqueta (sin distinguir mayúsculas ni tildes); opcional.</param>
public sealed record ConsultarCatalogoConsulta(string? Tipo, string? Q);

/// <summary>Busca en el catálogo NNN (solo entradas activas). No tiene datos de pacientes.</summary>
public sealed class ConsultarCatalogoNnn(IRepositorioCatalogoNnn catalogo)
{
    public const int MaximoResultados = 50;

    public async Task<Resultado<IReadOnlyList<EntradaCatalogoDto>>> EjecutarAsync(ConsultarCatalogoConsulta consulta, CancellationToken ct = default)
    {
        if (!TextosNnn.TryTaxonomia(consulta.Tipo, out var taxonomia) || taxonomia == TaxonomiaNnn.Indicador)
        {
            return ErroresComunes.ValidacionDeCampo("tipo", "El tipo debe ser nanda, nic o noc.");
        }

        var c = await catalogo.ObtenerAsync(ct);
        IEnumerable<EntradaCatalogoNnn> entradas = taxonomia switch
        {
            TaxonomiaNnn.Nanda => c.Diagnosticos,
            TaxonomiaNnn.Nic => c.Intervenciones,
            _ => c.Resultados,
        };

        var q = Normalizar(consulta.Q);
        return entradas
            .Where(e => e.Activo && (q.Length == 0 || e.Codigo.StartsWith(q, StringComparison.Ordinal) || Normalizar(e.Etiqueta).Contains(q, StringComparison.Ordinal)))
            .OrderBy(e => e.Codigo, StringComparer.Ordinal)
            .Take(MaximoResultados)
            .Select(EntradaCatalogoDto.Desde)
            .ToList();
    }

    /// <summary>Minúsculas y sin tildes: "Respiración" y "respiracion" se encuentran igual.</summary>
    public static string Normalizar(string? texto)
    {
        var descompuesto = (texto ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var ch in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>NIC y NOC sugeridos por el catálogo para un diagnóstico (para registrar uno manual con lo habitual).</summary>
public sealed class ObtenerVinculosDiagnostico(IRepositorioCatalogoNnn catalogo)
{
    public async Task<Resultado<VinculosDiagnosticoDto>> EjecutarAsync(string codigo, CancellationToken ct = default)
    {
        var c = await catalogo.ObtenerAsync(ct);
        if (c.Diagnostico(codigo) is not { } d)
        {
            return ErroresNnn.EntradaNoEncontrada;
        }

        var vinculos = c.VinculosDe(d.Codigo);
        return new VinculosDiagnosticoDto(
            DiagnosticoCatalogoDto.Desde(d),
            vinculos.Where(v => v.Tipo == TipoVinculoNnn.Nic).Select(v => c.Intervencion(v.Codigo)).OfType<IntervencionNic>()
                .Where(i => i.Activo).Select(EntradaCatalogoDto.Desde).ToList(),
            vinculos.Where(v => v.Tipo == TipoVinculoNnn.Noc).Select(v => c.Resultado(v.Codigo)).OfType<ResultadoNoc>()
                .Where(r => r.Activo)
                .Select(r => new ResultadoCatalogoDto(r.Codigo, r.Etiqueta, r.Escala, r.Edicion, r.Verificado,
                    c.IndicadoresDe(r.Codigo).Select(IndicadorDto.Desde).ToList()))
                .ToList());
    }
}

/// <param name="Tipo"><c>nanda</c>, <c>nic</c>, <c>noc</c> o <c>indicador</c>.</param>
public sealed record VerificarEntradaComando(string? Tipo, string? Codigo, Guid AdministradorId, string? Ip);

public sealed record EntradaVerificadaDto(string Tipo, string Codigo, string Etiqueta, string Edicion, bool Verificado, Guid? VerificadoPor, DateTime? VerificadoEn);

/// <summary>
/// El administrador marca una entrada como verificada cuando la enfermera asesora confirmó el código y la etiqueta contra su
/// libro. Desde entonces la semilla ya no la toca. Idempotente; queda auditado (es un dato del catálogo, no de un paciente).
/// </summary>
public sealed class VerificarEntradaCatalogoNnn(IRepositorioCatalogoNnn catalogo, IRepositorioAuditoria auditoria, IUnidadDeTrabajo unidadDeTrabajo, IReloj reloj)
{
    public async Task<Resultado<EntradaVerificadaDto>> EjecutarAsync(VerificarEntradaComando comando, CancellationToken ct = default)
    {
        if (!TextosNnn.TryTaxonomia(comando.Tipo, out var taxonomia))
        {
            return ErroresComunes.ValidacionDeCampo("tipo", "El tipo debe ser nanda, nic, noc o indicador.");
        }

        if (!CodigosNnn.EsValido(taxonomia, comando.Codigo))
        {
            return ErroresComunes.ValidacionDeCampo("codigo", "El código no tiene el formato de esa taxonomía.");
        }

        var entrada = await catalogo.ObtenerParaEditarAsync(taxonomia, comando.Codigo!, ct);
        if (entrada is null)
        {
            return ErroresNnn.EntradaNoEncontrada;
        }

        if (!entrada.Verificado)
        {
            var ahora = reloj.AhoraUtc;
            entrada.Verificar(comando.AdministradorId, ahora);
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.VerificarCatalogoNnn, ahora, comando.AdministradorId, comando.Ip, $"Catálogo NNN: entrada {taxonomia} verificada."));
            var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
            if (!guardado.EsExito)
            {
                return guardado.Error;
            }
        }

        return new EntradaVerificadaDto(
            comando.Tipo!.Trim().ToLowerInvariant(), entrada.Codigo, entrada.Etiqueta, entrada.Edicion, entrada.Verificado, entrada.VerificadoPor, entrada.VerificadoEn);
    }
}
