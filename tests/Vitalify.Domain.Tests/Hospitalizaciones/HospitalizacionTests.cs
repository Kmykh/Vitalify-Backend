using Vitalify.Domain.Comun;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Domain.Tests.Hospitalizaciones;

public class HospitalizacionTests
{
    private static readonly DateTime Ingreso = new(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Enfermera = Guid.NewGuid();

    private static Hospitalizacion Abrir() =>
        Hospitalizacion.Abrir(Guid.NewGuid(), Guid.NewGuid(), " Neumonía adquirida en la comunidad ", Ingreso, Enfermera);

    [Fact]
    public void Abrir_la_deja_activa_con_el_diagnostico_limpio()
    {
        var hospitalizacion = Abrir();

        Assert.True(hospitalizacion.EstaActiva);
        Assert.Equal("Neumonía adquirida en la comunidad", hospitalizacion.DiagnosticoIngreso);
        Assert.Equal(Enfermera, hospitalizacion.RegistradoPor);
        Assert.Null(hospitalizacion.EgresoEn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void El_diagnostico_debe_tener_al_menos_3_caracteres(string diagnostico)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Hospitalizacion.Abrir(Guid.NewGuid(), Guid.NewGuid(), diagnostico, Ingreso, Enfermera));
    }

    [Fact]
    public void Registrar_egreso_la_finaliza_y_conserva_los_datos()
    {
        var hospitalizacion = Abrir();
        var otraEnfermera = Guid.NewGuid();

        hospitalizacion.RegistrarEgreso(MotivoEgreso.AltaMedica, "  Evolución favorable ", Ingreso.AddDays(3), otraEnfermera);

        Assert.Equal(EstadoHospitalizacion.Finalizada, hospitalizacion.Estado);
        Assert.Equal(Ingreso.AddDays(3), hospitalizacion.EgresoEn);
        Assert.Equal(MotivoEgreso.AltaMedica, hospitalizacion.MotivoEgreso);
        Assert.Equal("Evolución favorable", hospitalizacion.ObservacionEgreso);
        Assert.Equal(otraEnfermera, hospitalizacion.EgresoRegistradoPor);
        Assert.Equal("Neumonía adquirida en la comunidad", hospitalizacion.DiagnosticoIngreso);
    }

    [Fact]
    public void No_se_puede_egresar_dos_veces_ni_editar_una_finalizada()
    {
        var hospitalizacion = Abrir();
        hospitalizacion.RegistrarEgreso(MotivoEgreso.Traslado, null, Ingreso.AddHours(5), Enfermera);

        Assert.Throws<ExcepcionDeDominio>(() => hospitalizacion.RegistrarEgreso(MotivoEgreso.AltaMedica, null, Ingreso.AddHours(6), Enfermera));
        Assert.Throws<ExcepcionDeDominio>(() => hospitalizacion.ActualizarDiagnostico("Otro diagnóstico"));
    }

    [Fact]
    public void El_egreso_no_puede_ser_anterior_al_ingreso_ni_tener_un_motivo_invalido()
    {
        var hospitalizacion = Abrir();

        Assert.Throws<ExcepcionDeDominio>(() => hospitalizacion.RegistrarEgreso(MotivoEgreso.AltaMedica, null, Ingreso.AddMinutes(-1), Enfermera));
        Assert.Throws<ExcepcionDeDominio>(() => hospitalizacion.RegistrarEgreso((MotivoEgreso)99, null, Ingreso.AddHours(1), Enfermera));
        Assert.True(hospitalizacion.EstaActiva);
    }

    [Fact]
    public void Vincular_asigna_el_dispositivo_y_liberar_lo_devuelve_a_disponible()
    {
        var hospitalizacion = Abrir();
        var dispositivo = Dispositivo.Registrar("ESP32-001", null);

        var asignacion = AsignacionDispositivo.Vincular(dispositivo, hospitalizacion, Ingreso.AddMinutes(10), Enfermera);

        Assert.True(asignacion.EstaVigente);
        Assert.Equal(EstadoDispositivo.Asignado, dispositivo.Estado);
        Assert.Equal((dispositivo.Id, hospitalizacion.Id), (asignacion.DispositivoId, asignacion.HospitalizacionId));

        asignacion.Liberar(dispositivo, MotivoLiberacion.Egreso, Ingreso.AddDays(2), Enfermera);

        Assert.False(asignacion.EstaVigente);
        Assert.Equal(MotivoLiberacion.Egreso, asignacion.MotivoLiberacion);
        Assert.Equal(Ingreso.AddDays(2), asignacion.LiberadoEn);
        Assert.Equal(EstadoDispositivo.Disponible, dispositivo.Estado);
    }

    [Fact]
    public void No_se_vincula_un_dispositivo_no_disponible_ni_a_una_hospitalizacion_finalizada()
    {
        var mantenimiento = Dispositivo.Registrar("ESP32-002", null);
        mantenimiento.EnviarAMantenimiento();
        Assert.Throws<ExcepcionDeDominio>(() => AsignacionDispositivo.Vincular(mantenimiento, Abrir(), Ingreso, Enfermera));

        var finalizada = Abrir();
        finalizada.RegistrarEgreso(MotivoEgreso.AltaMedica, null, Ingreso.AddDays(1), Enfermera);
        var disponible = Dispositivo.Registrar("ESP32-003", null);
        Assert.Throws<ExcepcionDeDominio>(() => AsignacionDispositivo.Vincular(disponible, finalizada, Ingreso.AddDays(1), Enfermera));
        Assert.Equal(EstadoDispositivo.Disponible, disponible.Estado);
    }

    [Fact]
    public void No_se_libera_dos_veces_ni_con_otro_dispositivo()
    {
        var dispositivo = Dispositivo.Registrar("ESP32-004", null);
        var asignacion = AsignacionDispositivo.Vincular(dispositivo, Abrir(), Ingreso, Enfermera);

        Assert.Throws<ExcepcionDeDominio>(() =>
            asignacion.Liberar(Dispositivo.Registrar("ESP32-005", null), MotivoLiberacion.Manual, Ingreso.AddHours(1), Enfermera));

        asignacion.Liberar(dispositivo, MotivoLiberacion.Manual, Ingreso.AddHours(1), Enfermera);
        Assert.Throws<ExcepcionDeDominio>(() => asignacion.Liberar(dispositivo, MotivoLiberacion.Manual, Ingreso.AddHours(2), Enfermera));
    }
}
