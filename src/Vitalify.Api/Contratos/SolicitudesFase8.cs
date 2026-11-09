namespace Vitalify.Api.Contratos;

/// <param name="Codigo">Código NIC de 4 dígitos, por ejemplo <c>3350</c>.</param>
/// <param name="Frecuencia"><c>UnaVez</c>, <c>CadaTurno</c>, <c>CadaHora</c> o <c>SegunNecesidad</c>.</param>
public sealed record IntervencionSolicitud(string? Codigo, string? Frecuencia);

/// <param name="Codigo">Código NOC de 4 dígitos, por ejemplo <c>0415</c>.</param>
/// <param name="PuntuacionBasal">De 1 a 5.</param>
/// <param name="PuntuacionMeta">De 1 a 5, mayor o igual que la basal.</param>
public sealed record ResultadoSolicitud(string? Codigo, int? PuntuacionBasal, int? PuntuacionMeta);

/// <summary>La enfermera confirma un diagnóstico NANDA-I (sugerido o elegido del catálogo) con sus NIC y NOC.</summary>
/// <param name="DiagnosticoCodigo">Código NANDA-I de 5 dígitos, por ejemplo <c>00032</c>.</param>
/// <param name="Origen"><c>Sugerido</c> (con <c>reglaOrigen</c>) o <c>Manual</c>.</param>
/// <param name="ReglaOrigen">La regla del motor que lo sugirió, por ejemplo <c>R1</c>.</param>
/// <param name="AlertaId">Alerta de la hospitalización que motivó el diagnóstico (opcional).</param>
/// <param name="Intervenciones">De 1 a 10.</param>
/// <param name="Resultados">De 1 a 10.</param>
public sealed record ConfirmarDiagnosticoSolicitud(
    string? DiagnosticoCodigo, string? Origen, string? ReglaOrigen, Guid? AlertaId,
    List<IntervencionSolicitud>? Intervenciones, List<ResultadoSolicitud>? Resultados);

/// <param name="DiagnosticoCodigo">El diagnóstico sugerido que se descarta.</param>
/// <param name="ReglaOrigen">La regla que lo sugirió.</param>
/// <param name="Motivo">Obligatorio, de 5 a 300 caracteres.</param>
public sealed record DescartarSugerenciaSolicitud(string? DiagnosticoCodigo, string? ReglaOrigen, string? Motivo);

/// <param name="Estado"><c>Resuelto</c> o <c>Descartado</c>. No se puede volver a <c>Activo</c>.</param>
/// <param name="Motivo">Obligatorio, de 5 a 300 caracteres.</param>
public sealed record CambiarEstadoDiagnosticoSolicitud(string? Estado, string? Motivo);

/// <param name="Codigo">Código NIC. El médico solo puede agregar <c>7710</c> (colaboración con el médico).</param>
/// <param name="Frecuencia"><c>UnaVez</c>, <c>CadaTurno</c>, <c>CadaHora</c> o <c>SegunNecesidad</c>.</param>
public sealed record AgregarIntervencionSolicitud(string? Codigo, string? Frecuencia);

/// <param name="Observacion">Opcional, hasta 500 caracteres (texto clínico: nunca va a los logs).</param>
/// <param name="AlertaId">Alerta por la que se ejecutó (opcional, de la misma hospitalización).</param>
public sealed record RegistrarEjecucionSolicitud(string? Observacion, Guid? AlertaId);

/// <param name="Codigo">Indicador NOC de 6 dígitos (por ejemplo <c>080204</c>) o el código del resultado para valorarlo completo.</param>
/// <param name="Puntuacion">De 1 a 5.</param>
public sealed record IndicadorValoradoSolicitud(string? Codigo, int? Puntuacion);

/// <param name="Indicadores">De 1 a 20, sin repetir.</param>
public sealed record RegistrarValoracionSolicitud(List<IndicadorValoradoSolicitud>? Indicadores);
