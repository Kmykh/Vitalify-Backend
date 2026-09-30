using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Sesiones;

/// <summary>
/// Sesión asociada a un refresh token. Solo se guarda el hash del token.
/// Cada refresh rota el token: la sesión actual queda revocada y reemplazada por una nueva que hereda
/// <see cref="ExpiraEn"/>, de modo que la duración máxima se cuenta desde el login y no desde el último refresh.
/// </summary>
public sealed class SesionRefresco
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string HashToken { get; private set; } = null!;
    public DateTime CreadaEn { get; private set; }
    public DateTime UltimoUsoEn { get; private set; }
    public DateTime ExpiraEn { get; private set; }
    public DateTime? RevocadaEn { get; private set; }
    public Guid? ReemplazadaPorId { get; private set; }

    private SesionRefresco()
    {
    }

    public bool EstaRevocada => RevocadaEn is not null;

    /// <summary>El token ya se usó para rotar. Si vuelve a presentarse, es un posible robo.</summary>
    public bool FueReemplazada => ReemplazadaPorId is not null;

    public static SesionRefresco Iniciar(Guid usuarioId, string hashToken, DateTime ahora, TimeSpan duracionMaxima)
    {
        if (duracionMaxima <= TimeSpan.Zero)
        {
            throw new ExcepcionDeDominio("La duración máxima de la sesión debe ser positiva.");
        }

        return Nueva(usuarioId, hashToken, ahora, ahora + duracionMaxima);
    }

    /// <summary>
    /// Activa si no fue revocada, no pasó su vencimiento y se usó hace menos de <paramref name="inactividadMaxima"/>.
    /// </summary>
    public bool EstaActiva(DateTime ahora, TimeSpan inactividadMaxima) =>
        !EstaRevocada && !Expiro(ahora, inactividadMaxima);

    /// <summary>Venció por tiempo máximo o por inactividad (independiente de si fue revocada).</summary>
    public bool Expiro(DateTime ahora, TimeSpan inactividadMaxima) =>
        ahora >= ExpiraEn || ahora - UltimoUsoEn >= inactividadMaxima;

    public void RegistrarUso(DateTime ahora)
    {
        if (ahora > UltimoUsoEn)
        {
            UltimoUsoEn = ahora;
        }
    }

    public void Revocar(DateTime ahora) => RevocadaEn ??= ahora;

    public SesionRefresco Rotar(string nuevoHashToken, DateTime ahora)
    {
        if (EstaRevocada)
        {
            throw new ExcepcionDeDominio("No se puede rotar una sesión revocada.");
        }

        RegistrarUso(ahora);
        var nueva = Nueva(UsuarioId, nuevoHashToken, ahora, ExpiraEn);
        ReemplazadaPorId = nueva.Id;
        Revocar(ahora);
        return nueva;
    }

    private static SesionRefresco Nueva(Guid usuarioId, string hashToken, DateTime ahora, DateTime expiraEn)
    {
        if (string.IsNullOrWhiteSpace(hashToken))
        {
            throw new ExcepcionDeDominio("El hash del token es obligatorio.");
        }

        return new SesionRefresco
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            HashToken = hashToken,
            CreadaEn = ahora,
            UltimoUsoEn = ahora,
            ExpiraEn = expiraEn,
        };
    }
}
