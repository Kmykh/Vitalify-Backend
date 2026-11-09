using Vitalify.Application.Comun;
using Vitalify.Domain.Nnn;

namespace Vitalify.Application.Nnn;

public static class ErroresNnn
{
    public static readonly Error EntradaNoEncontrada = Error.NoEncontrado("nnn-no-encontrado", "El código no está en el catálogo NNN.");

    public static readonly Error DiagnosticoNoEncontrado = Error.NoEncontrado("diagnostico-plan-no-encontrado", "El diagnóstico no existe en ningún plan de cuidados.");

    public static readonly Error IntervencionNoEncontrada = Error.NoEncontrado("intervencion-plan-no-encontrada", "La intervención no existe en ningún plan de cuidados.");

    public static readonly Error ResultadoNoEncontrado = Error.NoEncontrado("resultado-plan-no-encontrado", "El resultado NOC no existe en ningún plan de cuidados.");

    public static readonly Error DiagnosticoYaActivo = Error.Conflicto(
        "diagnostico-ya-activo", "Ese diagnóstico ya está activo en el plan de cuidados de esta hospitalización.");

    public static readonly Error DiagnosticoCerrado = Error.Conflicto(
        "diagnostico-cerrado", "El diagnóstico ya está resuelto o descartado; si reaparece, regístralo de nuevo.");

    public static readonly Error IntervencionInactiva = Error.Conflicto("intervencion-inactiva", "La intervención ya no está activa.");

    public static readonly Error IntervencionYaPlanificada = Error.Conflicto(
        "intervencion-ya-planificada", "Esa intervención ya está planificada y activa para este diagnóstico.");

    public static readonly Error SugerenciaConcurrente = Error.Conflicto(
        "sugerencia-concurrente", "La sugerencia se registró al mismo tiempo desde otro proceso.");

    public static readonly Error SoloEnfermeria = Error.Prohibido(
        "nnn-solo-enfermeria", $"El médico solo puede agregar o registrar la intervención NIC {ReglasNnn.NicColaboracionMedica} (colaboración con el médico).");
}
