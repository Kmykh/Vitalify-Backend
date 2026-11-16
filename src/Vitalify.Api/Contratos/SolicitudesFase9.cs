namespace Vitalify.Api.Contratos;

/// <summary>Confirmar una sugerencia con un toque: se planifica su paquete tal como viene.</summary>
/// <param name="DiagnosticoCodigo">El diagnóstico NANDA-I sugerido, por ejemplo <c>00032</c>.</param>
public sealed record ConfirmarPaqueteSolicitud(string? DiagnosticoCodigo);

/// <param name="Codigo">Indicador NOC de 6 dígitos o el código del resultado para valorarlo completo.</param>
/// <param name="Puntuacion">De 1 a 5.</param>
public sealed record IndicadorTurnoSolicitud(string? Codigo, int? Puntuacion);

/// <param name="ResultadoId">El resultado NOC del plan.</param>
/// <param name="Indicadores">De 1 a 20.</param>
public sealed record ResultadoTurnoSolicitud(Guid? ResultadoId, List<IndicadorTurnoSolicitud>? Indicadores);

/// <summary>La valoración NOC del turno de un paciente, en lote.</summary>
public sealed record ValoracionTurnoSolicitud(List<ResultadoTurnoSolicitud>? Resultados);

/// <summary>Cuántas valoraciones se guardaron.</summary>
public sealed record ValoracionTurnoRegistradaDto(int Valoraciones);

/// <summary>La nota del PAE del turno, revisada (y, si hizo falta, corregida) a partir del borrador.</summary>
/// <param name="Texto">De 5 a 4000 caracteres.</param>
public sealed record FirmarNotaTurnoSolicitud(string? Texto);
