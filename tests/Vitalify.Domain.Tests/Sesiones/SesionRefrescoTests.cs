using Vitalify.Domain.Comun;
using Vitalify.Domain.Sesiones;

namespace Vitalify.Domain.Tests.Sesiones;

public class SesionRefrescoTests
{
    private static readonly DateTime Login = new(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Inactividad = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Turno = TimeSpan.FromHours(12);

    private static SesionRefresco NuevaSesion() => SesionRefresco.Iniciar(Guid.NewGuid(), "hash-1", Login, Turno);

    [Fact]
    public void Recien_iniciada_esta_activa_y_vence_al_final_del_turno()
    {
        var sesion = NuevaSesion();

        Assert.True(sesion.EstaActiva(Login, Inactividad));
        Assert.Equal(Login + Turno, sesion.ExpiraEn);
        Assert.Equal(Login, sesion.UltimoUsoEn);
    }

    [Fact]
    public void Sigue_activa_justo_antes_del_limite_de_inactividad()
    {
        var sesion = NuevaSesion();
        Assert.True(sesion.EstaActiva(Login + Inactividad - TimeSpan.FromSeconds(1), Inactividad));
    }

    [Fact]
    public void Expira_al_cumplirse_el_tiempo_de_inactividad()
    {
        var sesion = NuevaSesion();

        Assert.False(sesion.EstaActiva(Login + Inactividad, Inactividad));
        Assert.True(sesion.Expiro(Login + Inactividad, Inactividad));
    }

    [Fact]
    public void Registrar_uso_reinicia_el_conteo_de_inactividad()
    {
        var sesion = NuevaSesion();

        sesion.RegistrarUso(Login.AddMinutes(20));

        Assert.True(sesion.EstaActiva(Login.AddMinutes(45), Inactividad));
        Assert.False(sesion.EstaActiva(Login.AddMinutes(50), Inactividad));
    }

    [Fact]
    public void Registrar_uso_con_una_fecha_anterior_no_retrocede_el_ultimo_uso()
    {
        var sesion = NuevaSesion();
        sesion.RegistrarUso(Login.AddMinutes(20));

        sesion.RegistrarUso(Login.AddMinutes(5));

        Assert.Equal(Login.AddMinutes(20), sesion.UltimoUsoEn);
    }

    [Fact]
    public void Expira_al_cumplirse_la_duracion_maxima_aunque_se_use_seguido()
    {
        var sesion = NuevaSesion();
        var fin = Login + Turno;
        sesion.RegistrarUso(fin - TimeSpan.FromMinutes(1));

        Assert.True(sesion.EstaActiva(fin - TimeSpan.FromSeconds(1), Inactividad));
        Assert.False(sesion.EstaActiva(fin, Inactividad));
    }

    [Fact]
    public void Revocada_no_esta_activa_y_conserva_la_primera_fecha_de_revocacion()
    {
        var sesion = NuevaSesion();

        sesion.Revocar(Login.AddMinutes(1));
        sesion.Revocar(Login.AddMinutes(2));

        Assert.False(sesion.EstaActiva(Login.AddMinutes(3), Inactividad));
        Assert.Equal(Login.AddMinutes(1), sesion.RevocadaEn);
    }

    [Fact]
    public void Rotar_revoca_la_actual_y_crea_otra_que_hereda_el_vencimiento_del_login()
    {
        var sesion = NuevaSesion();
        var momento = Login.AddMinutes(14);

        var nueva = sesion.Rotar("hash-2", momento);

        Assert.True(sesion.EstaRevocada);
        Assert.True(sesion.FueReemplazada);
        Assert.Equal(nueva.Id, sesion.ReemplazadaPorId);
        Assert.Equal(momento, sesion.UltimoUsoEn);

        Assert.Equal(sesion.UsuarioId, nueva.UsuarioId);
        Assert.Equal("hash-2", nueva.HashToken);
        Assert.Equal(sesion.ExpiraEn, nueva.ExpiraEn);
        Assert.True(nueva.EstaActiva(momento, Inactividad));
    }

    [Fact]
    public void No_se_puede_rotar_una_sesion_revocada()
    {
        var sesion = NuevaSesion();
        sesion.Revocar(Login.AddMinutes(1));

        Assert.Throws<ExcepcionDeDominio>(() => sesion.Rotar("hash-2", Login.AddMinutes(2)));
    }

    [Fact]
    public void Requiere_hash_y_duracion_positiva()
    {
        Assert.Throws<ExcepcionDeDominio>(() => SesionRefresco.Iniciar(Guid.NewGuid(), "", Login, Turno));
        Assert.Throws<ExcepcionDeDominio>(() => SesionRefresco.Iniciar(Guid.NewGuid(), "hash", Login, TimeSpan.Zero));
    }
}
