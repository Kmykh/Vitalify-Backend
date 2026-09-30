namespace Vitalify.Domain.Comun;

/// <summary>
/// Se lanza cuando se intenta violar una invariante del dominio. La capa de aplicación valida antes de
/// llegar aquí, así que en el flujo normal no debería ocurrir.
/// </summary>
public sealed class ExcepcionDeDominio(string mensaje) : Exception(mensaje);
