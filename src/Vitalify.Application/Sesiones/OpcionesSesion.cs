namespace Vitalify.Application.Sesiones;

/// <param name="InactividadMaxima">Tiempo sin usar el refresh token tras el cual la sesión expira (HU04 E2).</param>
/// <param name="DuracionMaxima">Duración máxima de una sesión desde el login (un turno).</param>
public sealed record OpcionesSesion(TimeSpan InactividadMaxima, TimeSpan DuracionMaxima);
