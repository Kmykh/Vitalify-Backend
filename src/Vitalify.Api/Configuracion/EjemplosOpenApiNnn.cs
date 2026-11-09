using System.Text.Json.Nodes;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Vitalify.Api.Configuracion;

/// <summary>
/// Ejemplos de cuerpo y de respuesta de los endpoints del proceso de enfermería NNN (fase 8), para que Swagger muestre un
/// caso clínico real (escenario E1: FR 26 y SpO2 90 del wearable). Los ids son ilustrativos.
/// </summary>
internal sealed class EjemplosOpenApiNnn : IOperationFilter
{
    private const string Diagnostico = """
        {
          "id": "6f1c2a4e-7d3b-4c8e-9a51-2b7e0d4f8c11", "pacienteId": "0b9a7e52-3c1d-4f6a-8e2b-5d4c3b2a1f00",
          "hospitalizacionId": "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d", "codigo": "00032", "etiqueta": "Patrón respiratorio ineficaz",
          "tipo": "Real", "verificado": false, "origen": "Sugerido", "reglaOrigen": "R1", "alertaId": "5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716",
          "evaluacionId": "9c8b7a65-4321-4fed-8cba-987654321abc", "estado": "Activo", "motivoCierre": null,
          "confirmadoPor": { "id": "3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a", "nombre": "Lic. Rosa Quispe", "rol": "Enfermera" },
          "confirmadoEn": "2026-11-09T15:04:12Z", "cerradoPor": null, "cerradoEn": null, "versionReglas": "2026.11.1",
          "intervenciones": [
            { "id": "7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d", "codigo": "3350", "etiqueta": "Monitorización respiratoria", "verificado": false,
              "frecuencia": "CadaHora", "activa": true, "agregadaPor": { "id": "3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a", "nombre": "Lic. Rosa Quispe", "rol": "Enfermera" },
              "agregadaEn": "2026-11-09T15:04:12Z", "ultimaEjecucionEn": null, "ejecuciones": [] }
          ],
          "resultados": [
            { "id": "2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e", "codigo": "0802", "etiqueta": "Signos vitales", "verificado": false,
              "escala": "Desviación grave del rango normal (1) a Sin desviación del rango normal (5)", "puntuacionBasal": 2, "puntuacionMeta": 4,
              "ultimaPuntuacion": null, "ultimaValoracionEn": null,
              "indicadores": [ { "codigo": "080204", "etiqueta": "Frecuencia respiratoria", "parametroVital": "fr", "verificado": false } ],
              "valoraciones": [] }
          ]
        }
        """;

    private static readonly Dictionary<string, (string? Cuerpo, string Respuesta)> Ejemplos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET api/v1/nnn/catalogo"] = (null, """
            [ { "codigo": "00030", "etiqueta": "Deterioro del intercambio de gases", "edicion": "NANDA-I 2024-2026", "verificado": false },
              { "codigo": "00032", "etiqueta": "Patrón respiratorio ineficaz", "edicion": "NANDA-I 2024-2026", "verificado": false } ]
            """),
        ["GET api/v1/nnn/diagnosticos/{codigo}/vinculos"] = (null, """
            { "diagnostico": { "codigo": "00032", "etiqueta": "Patrón respiratorio ineficaz", "dominio": "Actividad/reposo",
                "clase": "Respuestas cardiovasculares/pulmonares", "tipo": "Real", "edicion": "NANDA-I 2024-2026", "verificado": false },
              "intervenciones": [ { "codigo": "3350", "etiqueta": "Monitorización respiratoria", "edicion": "NIC 8a ed.", "verificado": false },
                                  { "codigo": "3140", "etiqueta": "Manejo de la vía aérea", "edicion": "NIC 8a ed.", "verificado": false } ],
              "resultados": [ { "codigo": "0802", "etiqueta": "Signos vitales", "escala": "Desviación grave del rango normal (1) a Sin desviación del rango normal (5)",
                                "edicion": "NOC 7a ed.", "verificado": false,
                                "indicadores": [ { "codigo": "080204", "etiqueta": "Frecuencia respiratoria", "parametroVital": "fr", "verificado": false } ] } ] }
            """),
        ["PUT api/v1/nnn/catalogo/{tipo}/{codigo}/verificacion"] = (null, """
            { "tipo": "nanda", "codigo": "00032", "etiqueta": "Patrón respiratorio ineficaz", "edicion": "NANDA-I 2024-2026", "verificado": true,
              "verificadoPor": "1f0e9d8c-7b6a-4594-8372-61504f3e2d1c", "verificadoEn": "2026-11-09T16:00:00Z" }
            """),
        ["GET api/v1/pacientes/{id}/nnn/sugerencias"] = (null, """
            { "pacienteId": "0b9a7e52-3c1d-4f6a-8e2b-5d4c3b2a1f00", "hospitalizacionId": "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d",
              "versionReglas": "2026.11.1", "evaluacionId": "9c8b7a65-4321-4fed-8cba-987654321abc", "evaluadaEn": "2026-11-09T15:00:00Z",
              "news2": 6, "evaluacionCompleta": false,
              "sugerencias": [
                { "diagnostico": { "codigo": "00032", "etiqueta": "Patrón respiratorio ineficaz", "verificado": false },
                  "intervenciones": [ { "codigo": "3350", "etiqueta": "Monitorización respiratoria", "verificado": false },
                                      { "codigo": "3140", "etiqueta": "Manejo de la vía aérea", "verificado": false },
                                      { "codigo": "7710", "etiqueta": "Colaboración con el médico", "verificado": false } ],
                  "resultados": [ { "codigo": "0415", "etiqueta": "Estado respiratorio", "verificado": false } ],
                  "regla": "R1", "reglas": [ "R1", "R11" ],
                  "motivo": "FR 26 rpm: 3 puntos NEWS2. NEWS2 6 con un parámetro en 3: avise al médico",
                  "prioridad": "alta", "evaluacionId": "9c8b7a65-4321-4fed-8cba-987654321abc", "alertaId": "5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716" }
              ],
              "aviso": "Vitalify sugiere; la enfermera decide." }
            """),
        ["GET api/v1/alertas/{id}/sugerencias-nnn"] = (null, """
            { "alertaId": "5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716", "pacienteId": "0b9a7e52-3c1d-4f6a-8e2b-5d4c3b2a1f00",
              "hospitalizacionId": "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d", "versionReglas": "2026.11.1",
              "evaluacionId": "9c8b7a65-4321-4fed-8cba-987654321abc", "evaluadaEn": "2026-11-09T15:00:00Z",
              "sugerencias": [
                { "diagnostico": { "codigo": "00030", "etiqueta": "Deterioro del intercambio de gases", "verificado": false },
                  "intervenciones": [ { "codigo": "3320", "etiqueta": "Oxigenoterapia", "verificado": false },
                                      { "codigo": "3350", "etiqueta": "Monitorización respiratoria", "verificado": false },
                                      { "codigo": "7710", "etiqueta": "Colaboración con el médico", "verificado": false } ],
                  "resultados": [ { "codigo": "0402", "etiqueta": "Estado respiratorio: intercambio gaseoso", "verificado": false } ],
                  "regla": "R2", "reglas": [ "R2", "R11" ], "motivo": "SpO2 90 % (escala 1): 3 puntos NEWS2. NEWS2 6 con un parámetro en 3: avise al médico",
                  "prioridad": "alta", "evaluacionId": "9c8b7a65-4321-4fed-8cba-987654321abc", "alertaId": "5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716" }
              ],
              "aviso": "Vitalify sugiere; la enfermera decide." }
            """),
        ["GET api/v1/pacientes/{id}/plan-cuidados"] = (null, $$"""
            { "pacienteId": "0b9a7e52-3c1d-4f6a-8e2b-5d4c3b2a1f00", "hospitalizacionId": "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d",
              "hospitalizacionActiva": true, "versionReglas": "2026.11.1", "diagnosticos": [ {{Diagnostico}} ] }
            """),
        ["POST api/v1/pacientes/{id}/plan-cuidados/diagnosticos"] = ("""
            { "diagnosticoCodigo": "00032", "origen": "Sugerido", "reglaOrigen": "R1", "alertaId": null,
              "intervenciones": [ { "codigo": "3350", "frecuencia": "CadaHora" }, { "codigo": "3140", "frecuencia": "CadaTurno" } ],
              "resultados": [ { "codigo": "0415", "puntuacionBasal": 2, "puntuacionMeta": 4 }, { "codigo": "0802", "puntuacionBasal": 2, "puntuacionMeta": 4 } ] }
            """, Diagnostico),
        ["POST api/v1/pacientes/{id}/plan-cuidados/sugerencias/descartar"] = ("""
            { "diagnosticoCodigo": "00030", "reglaOrigen": "R2", "motivo": "Sensor mal colocado: SpO2 98 % con oxímetro de pulso." }
            """, """
            { "id": "8e7d6c5b-4a39-4281-9706-f5e4d3c2b1a0", "pacienteId": "0b9a7e52-3c1d-4f6a-8e2b-5d4c3b2a1f00",
              "hospitalizacionId": "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d", "codigo": "00030", "etiqueta": "Deterioro del intercambio de gases",
              "tipo": "Real", "verificado": false, "origen": "Sugerido", "reglaOrigen": "R2", "alertaId": null, "evaluacionId": "9c8b7a65-4321-4fed-8cba-987654321abc",
              "estado": "Descartado", "motivoCierre": "Sensor mal colocado: SpO2 98 % con oxímetro de pulso.", "confirmadoPor": null, "confirmadoEn": null,
              "cerradoPor": { "id": "3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a", "nombre": "Lic. Rosa Quispe", "rol": "Enfermera" },
              "cerradoEn": "2026-11-09T15:06:00Z", "versionReglas": "2026.11.1", "intervenciones": [], "resultados": [] }
            """),
        ["PUT api/v1/plan-cuidados/diagnosticos/{id}/estado"] = ("""
            { "estado": "Resuelto", "motivo": "FR 18 rpm sostenida durante 6 h sin oxígeno." }
            """, Diagnostico.Replace("\"estado\": \"Activo\", \"motivoCierre\": null", "\"estado\": \"Resuelto\", \"motivoCierre\": \"FR 18 rpm sostenida durante 6 h sin oxígeno.\"", StringComparison.Ordinal)),
        ["POST api/v1/plan-cuidados/diagnosticos/{id}/intervenciones"] = ("""
            { "codigo": "7710", "frecuencia": "UnaVez" }
            """, """
            { "id": "4c5d6e7f-8091-4a2b-b3c4-d5e6f708192a", "codigo": "7710", "etiqueta": "Colaboración con el médico", "verificado": false,
              "frecuencia": "UnaVez", "activa": true, "agregadaPor": { "id": "6a5b4c3d-2e1f-4a0b-9c8d-7e6f5a4b3c2d", "nombre": "Dr. Luis Paredes", "rol": "Medico" },
              "agregadaEn": "2026-11-09T15:10:00Z", "ultimaEjecucionEn": null, "ejecuciones": [] }
            """),
        ["POST api/v1/plan-cuidados/intervenciones/{id}/ejecuciones"] = ("""
            { "observacion": "FR contada en 1 minuto: 24 rpm; se elevó la cabecera a 45°.", "alertaId": "5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716" }
            """, """
            { "id": "1a2b3c4d-5e6f-4708-9a1b-2c3d4e5f6071", "planIntervencionId": "7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d",
              "ejecutadaPor": { "id": "3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a", "nombre": "Lic. Rosa Quispe", "rol": "Enfermera" },
              "ejecutadaEn": "2026-11-09T15:20:00Z", "alertaId": "5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716",
              "observacion": "FR contada en 1 minuto: 24 rpm; se elevó la cabecera a 45°." }
            """),
        ["POST api/v1/plan-cuidados/resultados/{id}/valoraciones"] = ("""
            { "indicadores": [ { "codigo": "080204", "puntuacion": 2 }, { "codigo": "0802", "puntuacion": 2 } ] }
            """, """
            { "planResultadoId": "2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e", "valoradaEn": "2026-11-09T15:30:00Z", "turno": "2026-11-09/GuardiaDiurna",
              "indicadores": [
                { "id": "0f1e2d3c-4b5a-4697-8877-665544332211", "indicadorCodigo": "080204", "etiqueta": "Frecuencia respiratoria", "puntuacion": 2,
                  "puntuacionSugerida": 1, "valoradaPor": { "id": "3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a", "nombre": "Lic. Rosa Quispe", "rol": "Enfermera" },
                  "valoradaEn": "2026-11-09T15:30:00Z", "turno": "2026-11-09/GuardiaDiurna" },
                { "id": "1e2d3c4b-5a69-4788-9766-554433221100", "indicadorCodigo": null, "etiqueta": "Signos vitales (global)", "puntuacion": 2,
                  "puntuacionSugerida": null, "valoradaPor": { "id": "3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a", "nombre": "Lic. Rosa Quispe", "rol": "Enfermera" },
                  "valoradaEn": "2026-11-09T15:30:00Z", "turno": "2026-11-09/GuardiaDiurna" } ] }
            """),
        ["GET api/v1/plan-cuidados/resultados/{id}/valoracion-sugerida"] = (null, """
            { "planResultadoId": "2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e", "resultadoCodigo": "0802", "etiqueta": "Signos vitales",
              "escala": "Desviación grave del rango normal (1) a Sin desviación del rango normal (5)",
              "evaluacionId": "9c8b7a65-4321-4fed-8cba-987654321abc", "evaluadaEn": "2026-11-09T15:00:00Z",
              "indicadores": [
                { "codigo": "080204", "etiqueta": "Frecuencia respiratoria", "parametroVital": "fr", "puntuacionSugerida": 1, "ultimaPuntuacion": null },
                { "codigo": "080208", "etiqueta": "Saturación de oxígeno", "parametroVital": "spo2", "puntuacionSugerida": 1, "ultimaPuntuacion": null },
                { "codigo": "080205", "etiqueta": "Presión arterial sistólica", "parametroVital": "pas", "puntuacionSugerida": null, "ultimaPuntuacion": null },
                { "codigo": "0802", "etiqueta": "Signos vitales (global)", "parametroVital": null, "puntuacionSugerida": null, "ultimaPuntuacion": null } ] }
            """),
        ["GET api/v1/sistema/indicadores-nnn"] = (null, """
            { "desde": "2026-10-10T00:00:00Z", "hasta": "2026-11-09T00:00:00Z", "versionReglas": "2026.11.1",
              "sugerencias": { "emitidas": 42, "confirmadas": 28, "descartadas": 9, "expiradas": 4, "pendientes": 1, "tasaAceptacion": 0.7568,
                "tasaAceptacionSobreEmitidas": 0.6667,
                "porRegla": [ { "regla": "R1", "emitidas": 12, "confirmadas": 10, "descartadas": 1, "expiradas": 1, "pendientes": 0, "tasaAceptacion": 0.9091 } ] },
              "alertaAPrimeraIntervencion": { "alertas": 17, "minutosPromedio": 6.4, "minutosMediana": 4.5 },
              "valoracionNocPorTurno": { "diagnosticos": 25, "diagnosticosConValoracionEnCadaTurno": 19, "porcentaje": 76.0, "turnosEvaluados": 61, "turnosConValoracion": 53 },
              "cambioNoc": { "resultadosDeDiagnosticosResueltos": 30, "resultadosConValoracion": 27, "cambioMedio": 1.48 } }
            """),
        ["POST api/v1/alertas/{id}/atender"] = ("""
            { "nota": "Semifowler, FR y SpO2 cada hora; se avisó al médico de turno.",
              "intervencionesEjecutadas": [ "7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d" ] }
            """, """
            { "id": "5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716", "tipo": "riesgo", "prioridad": "media", "estado": "atendida" }
            """),
    };

    /// <summary>Las rutas que deben tener ejemplo (las nuevas de la fase 8 y atender con intervenciones).</summary>
    public static IReadOnlyCollection<string> Rutas => Ejemplos.Keys;

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var clave = $"{context.ApiDescription.HttpMethod} {context.ApiDescription.RelativePath}";
        if (!Ejemplos.TryGetValue(clave, out var ejemplo))
        {
            return;
        }

        if (ejemplo.Cuerpo is not null && operation.RequestBody?.Content is { } cuerpo)
        {
            foreach (var tipo in cuerpo.Values)
            {
                tipo.Example = Convertir(JsonNode.Parse(ejemplo.Cuerpo));
            }
        }

        var exito = operation.Responses.FirstOrDefault(r => r.Key.StartsWith('2')).Value;
        if (exito?.Content is { } respuesta)
        {
            foreach (var tipo in respuesta.Values)
            {
                tipo.Example = Convertir(JsonNode.Parse(ejemplo.Respuesta));
            }
        }
    }

    private static IOpenApiAny Convertir(JsonNode? nodo) => nodo switch
    {
        null => new OpenApiNull(),
        JsonObject o => o.Aggregate(new OpenApiObject(), (obj, p) =>
        {
            obj[p.Key] = Convertir(p.Value);
            return obj;
        }),
        JsonArray a => a.Aggregate(new OpenApiArray(), (arr, x) =>
        {
            arr.Add(Convertir(x));
            return arr;
        }),
        JsonValue v when v.TryGetValue<bool>(out var b) => new OpenApiBoolean(b),
        JsonValue v when v.TryGetValue<long>(out var l) => new OpenApiLong(l),
        JsonValue v when v.TryGetValue<double>(out var d) => new OpenApiDouble(d),
        JsonValue v => new OpenApiString(v.ToString()),
        _ => new OpenApiNull(),
    };
}
