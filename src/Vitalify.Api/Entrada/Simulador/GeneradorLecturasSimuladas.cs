using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Api.Entrada.Simulador;

/// <summary>
/// Genera lecturas del contrato de telemetría según el escenario de cada dispositivo. Es determinista: con la
/// misma semilla y las mismas horas produce exactamente la misma secuencia (cada dispositivo tiene su propio
/// <see cref="Random"/>, sembrado con la semilla y el código). <c>seq</c> es creciente por dispositivo.
/// </summary>
public sealed class GeneradorLecturasSimuladas(OpcionesSimulador opciones)
{
    private readonly Dictionary<string, EstadoSimulado> _estados = new(StringComparer.Ordinal);

    public LecturaTelemetria Generar(string codigoDispositivo, DateTime ahora)
    {
        if (!_estados.TryGetValue(codigoDispositivo, out var estado))
        {
            estado = new EstadoSimulado(new Random(opciones.Semilla ^ HashEstable(codigoDispositivo)), ahora);
            _estados[codigoDispositivo] = estado;
        }

        var seq = ++estado.Seq;
        var r = estado.Random;
        var bateria = Math.Max(5, 100 - (int)(seq / 30));

        var estable = new SignosMedidos(
            Fc: r.Next(70, 86), Fr: r.Next(14, 19), Spo2: r.Next(96, 100), Temperatura: Decimal(r, 36.5, 37.2),
            Pas: r.Next(110, 126), Pad: r.Next(70, 81), Bateria: bateria);

        var (signos, caida) = opciones.EscenarioDe(codigoDispositivo) switch
        {
            EscenarioSimulacion.Deterioro => (Deteriorar(estable, Progreso(estado.Inicio, ahora)), false),
            EscenarioSimulacion.Caida => (estable, seq % opciones.LecturasEntreCaidas == 0),
            EscenarioSimulacion.SensorDefectuoso => (Defectuoso(estable, seq), false),
            _ => (estable, false),
        };

        var medidoEn = new DateTime(ahora.Ticks - (ahora.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        return new LecturaTelemetria(codigoDispositivo, medidoEn, seq, signos, caida, OrigenLectura.Simulador);
    }

    /// <summary>De 0 (inicio) a 1 (tras <see cref="OpcionesSimulador.MinutosDeterioro"/>).</summary>
    private double Progreso(DateTime inicio, DateTime ahora) =>
        Math.Clamp((ahora - inicio).TotalMinutes / opciones.MinutosDeterioro, 0, 1);

    private static SignosMedidos Deteriorar(SignosMedidos s, double p) => s with
    {
        Fc = s.Fc + (decimal)Math.Round(45 * p),
        Fr = s.Fr + (decimal)Math.Round(12 * p),
        Spo2 = s.Spo2 - (decimal)Math.Round(10 * p),
        Temperatura = s.Temperatura + Math.Round((decimal)(1.8 * p), 1),
        Pas = s.Pas - (decimal)Math.Round(30 * p),
        Pad = s.Pad - (decimal)Math.Round(15 * p),
    };

    /// <summary>
    /// Patrón fijo por <c>seq</c>: FC imposible cada 4 lecturas (HU10 E2), presión sin diastólica cada 5 (HU12 E2)
    /// y sin temperatura en las lecturas 6 a 17 de cada 24, más de 90 s a 10 s por lectura (HU11 E2).
    /// </summary>
    private static SignosMedidos Defectuoso(SignosMedidos s, long seq)
    {
        if (seq % 4 == 0)
        {
            s = s with { Fc = 400 };
        }

        if (seq % 5 == 0)
        {
            s = s with { Pad = null };
        }

        if (seq % 24 is >= 6 and < 18)
        {
            s = s with { Temperatura = null };
        }

        return s;
    }

    private static decimal Decimal(Random r, double minimo, double maximo) =>
        Math.Round((decimal)(minimo + (r.NextDouble() * (maximo - minimo))), 1);

    /// <summary>FNV-1a: estable entre ejecuciones (<c>string.GetHashCode</c> cambia en cada proceso).</summary>
    private static int HashEstable(string texto)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in texto)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }

    private sealed class EstadoSimulado(Random random, DateTime inicio)
    {
        public Random Random { get; } = random;
        public DateTime Inicio { get; } = inicio;
        public long Seq { get; set; }
    }
}
