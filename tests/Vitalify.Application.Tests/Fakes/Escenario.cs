using Vitalify.Application.Alertas;
using Vitalify.Application.Auditoria;
using Vitalify.Application.Camas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Historial;
using Vitalify.Application.Nnn;
using Vitalify.Application.Notas;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Turnos;
using Vitalify.Application.Usuarios;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Fakes;

/// <summary>Arma los casos de uso sobre los fakes en memoria.</summary>
public sealed class Escenario
{
    public static readonly OpcionesSesion Opciones = new(TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));

    public RelojFalso Reloj { get; } = new();
    public HasherFalso Hasher { get; } = new();
    public GeneradorTokensFalso Generador { get; } = new();
    public RepositorioUsuariosEnMemoria Usuarios { get; } = new();
    public RepositorioSesionesEnMemoria Sesiones { get; } = new();
    public RepositorioTokensRevocadosEnMemoria TokensRevocados { get; } = new();
    public RepositorioAuditoriaEnMemoria Auditoria { get; } = new();
    public UnidadDeTrabajoFalsa UnidadDeTrabajo { get; } = new();

    public RepositorioPacientesEnMemoria Pacientes { get; } = new();
    public RepositorioHospitalizacionesEnMemoria Hospitalizaciones { get; } = new();
    public RepositorioCamasEnMemoria Camas { get; }
    public RepositorioDispositivosEnMemoria Dispositivos { get; }

    public Guid EnfermeraId { get; } = Guid.NewGuid();

    public ResolutorDispositivosFalso Resolutor { get; }
    public HistorialEnMemoria Historial { get; } = new();
    public SolicitudNuevaLecturaFalsa SolicitudNuevaLectura { get; } = new();
    public ManejadorLecturaRegistradaFalso Manejador { get; } = new();
    public PresenciaEnMemoria Presencia { get; } = new();
    public OpcionesClinicas Clinicas { get; set; } = OpcionesClinicas.PorDefecto;
    public RepositorioCodigosEnMemoria Codigos { get; } = new();
    public PublicadorComandosFalso Comandos { get; } = new();
    public OpcionesVinculacion Vinculacion { get; set; } = OpcionesVinculacion.PorDefecto;

    public RepositorioServiciosEnMemoria Servicios { get; } = new();
    public RepositorioProgramacionEnMemoria Programacion { get; } = new();
    public RepositorioAlertasEnMemoria Alertas { get; }
    public RepositorioMovilesEnMemoria Moviles { get; } = new();
    public PushFalso Push { get; } = new();
    public TiempoRealFalso TiempoReal { get; } = new();
    public BitacoraFalsa Bitacora { get; } = new();
    public OpcionesAlertas OpcionesAlertas { get; set; } = OpcionesAlertas.PorDefecto;
    public CatalogoTurnos Catalogo { get; } = new(CatalogoTurnos.HorariosPorDefecto, TimeZoneInfo.FindSystemTimeZoneById("America/Lima"));
    public LimitadorSignosTiempoReal Limitador { get; } = new();
    public RepositorioNotasEnMemoria Notas { get; } = new();
    public RepositorioCatalogoNnnEnMemoria CatalogoNnn { get; } = new();
    public RepositorioPlanCuidadosEnMemoria Plan { get; } = new();

    public bool TiempoRealFalla { get => TiempoReal.Falla; set => TiempoReal.Falla = value; }

    /// <summary>Servicio de las camas y del personal que crea el escenario.</summary>
    public Servicio Servicio { get; } = Servicio.Registrar("MEDICINA-B", "Medicina B");

    public Escenario()
    {
        Servicios.Agregar(Servicio);
        Camas = new RepositorioCamasEnMemoria(Hospitalizaciones.Ocupada, Servicios);
        Dispositivos = new RepositorioDispositivosEnMemoria(Hospitalizaciones.CamaDe);
        Hospitalizaciones.Pacientes = Pacientes;
        Hospitalizaciones.Camas = Camas;
        Hospitalizaciones.Dispositivos = Dispositivos;
        Hospitalizaciones.Servicios = Servicios;
        Resolutor = new ResolutorDispositivosFalso(Hospitalizaciones, Dispositivos);
        Alertas = new RepositorioAlertasEnMemoria(Hospitalizaciones);
    }

    public RegistrarUsuario RegistrarUsuario() =>
        new(new RegistrarUsuarioValidador(), Usuarios, Auditoria, UnidadDeTrabajo, Hasher, Servicios, Reloj);

    public IniciarSesion IniciarSesion() =>
        new(new IniciarSesionValidador(), Usuarios, Sesiones, Auditoria, UnidadDeTrabajo, Hasher, Generador, Reloj, Opciones);

    public RefrescarSesion RefrescarSesion() =>
        new(new RefrescarSesionValidador(), Usuarios, Sesiones, Auditoria, UnidadDeTrabajo, Generador, Reloj, Opciones);

    public CerrarSesion CerrarSesion() =>
        new(Sesiones, TokensRevocados, Auditoria, UnidadDeTrabajo, Generador, Reloj);

    public ListarUsuarios ListarUsuarios() => new(new ListarUsuariosValidador(), Usuarios);

    public ObtenerUsuarioActual ObtenerUsuarioActual() => new(Usuarios);

    public RegistrarCama RegistrarCama() => new(new RegistrarCamaValidador(), Camas, Servicios, UnidadDeTrabajo);

    public ListarCamas ListarCamas() => new(Camas);

    public RegistrarDispositivo RegistrarDispositivo() => new(new RegistrarDispositivoValidador(), Dispositivos, UnidadDeTrabajo);

    public CambiarEstadoDispositivo CambiarEstadoDispositivo() =>
        new(new CambiarEstadoDispositivoValidador(), Dispositivos, UnidadDeTrabajo);

    public ListarDispositivos ListarDispositivos() => new(Dispositivos);

    public RegistrarIngresoPaciente RegistrarIngresoPaciente() =>
        new(new RegistrarIngresoPacienteValidador(Reloj), Pacientes, Camas, Servicios, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Reloj);

    public ActualizarDatosPaciente ActualizarDatosPaciente() =>
        new(new ActualizarDatosPacienteValidador(Reloj), Pacientes, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Reloj);

    public VincularDispositivo VincularDispositivo() =>
        new(Pacientes, Dispositivos, Camas, Hospitalizaciones, Codigos, Auditoria, UnidadDeTrabajo, Resolutor, ServicioCodigos(), Reloj);

    public LiberarDispositivo LiberarDispositivo() =>
        new(Pacientes, Dispositivos, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Resolutor, ServicioCodigos(), Reloj);

    public RegistrarEgreso RegistrarEgreso() =>
        new(new RegistrarEgresoValidador(), Pacientes, Dispositivos, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Resolutor, ServicioCodigos(), Reloj,
            new CierrePlanCuidados(Plan, Auditoria), NotificadorPlan());

    public ServicioCodigosVinculacion ServicioCodigos() =>
        new(Codigos, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Comandos, Vinculacion, Reloj);

    public SincronizarPantallaDispositivo SincronizarPantalla() => new(Dispositivos, Codigos, ServicioCodigos(), Comandos, Reloj);

    public AtenderSolicitudDispositivo AtenderSolicitud() => new(SincronizarPantalla());

    public RegenerarQrDispositivo RegenerarQr() => new(Dispositivos, ServicioCodigos());

    public EscanearQr EscanearQr() =>
        new(Dispositivos, Codigos, Presencia, ServicioCodigos(), Comandos, Auditoria, UnidadDeTrabajo, Vinculacion, Reloj);

    public RenovarCodigosVinculacion RenovarCodigos() => new(Presencia, Dispositivos, Codigos, ServicioCodigos(), Reloj);

    public ConfirmarVinculacion ConfirmarVinculacion() =>
        new(new RegistrarIngresoPacienteValidador(Reloj),
            new TomaInicialValidador(Domain.Telemetria.RangosFisiologicos.PorDefecto, Clinicas, Reloj),
            Codigos, Dispositivos, Pacientes, RegistrarIngresoPaciente(), VincularDispositivo(), ServicioObservaciones(), ServicioCodigos(),
            Auditoria, UnidadDeTrabajo, Resolutor, Reloj);

    public ListarPacientesMonitoreados ListarPacientesMonitoreados() =>
        new(Hospitalizaciones, Historial, Historial, Alertas, Presencia, OpcionesTelemetria.PorDefecto, Reloj);

    /// <summary>Con el motor clínico enganchado, como en la API.</summary>
    public RegistrarLectura RegistrarLectura() =>
        new(Resolutor, Historial, Historial, Historial, Historial, SolicitudNuevaLectura,
            [
                Manejador,
                new EvaluarRiesgoAlRegistrarLectura(Evaluador(), Historial, Historial, PublicadorRiesgo()),
                new AlertasAlRegistrarLectura(ServicioAlertas()),
                new PublicarSignosEnTiempoReal(Contextos(), TiempoReal, Limitador, Reloj),
            ],
            Domain.Telemetria.RangosFisiologicos.PorDefecto, OpcionesTelemetria.PorDefecto, Bitacora, Reloj);

    public ObtenerSignosActuales ObtenerSignosActuales() =>
        new(Pacientes, Hospitalizaciones, Historial, Presencia, OpcionesTelemetria.PorDefecto, Reloj);

    public ObtenerHistorialSignos ObtenerHistorialSignos() => new(Episodios(), Historial, AuditoriaHistorial(), Reloj);

    public EvaluadorRiesgo Evaluador() => new(Historial, Historial, Historial, OpcionesTelemetria.PorDefecto, Clinicas, Reloj);

    public PresentadorRiesgo PresentadorRiesgo() => new(Historial);

    public ServicioObservaciones ServicioObservaciones() =>
        new(Historial, Evaluador(), Historial, PresentadorRiesgo(), PublicadorRiesgo(), ServicioAlertas(), Bitacora, Clinicas,
            Domain.Telemetria.RangosFisiologicos.PorDefecto);

    public RegistrarObservacion RegistrarObservacion() =>
        new(new TomaManualValidador(Domain.Telemetria.RangosFisiologicos.PorDefecto, Clinicas, Reloj),
            Pacientes, Hospitalizaciones, ServicioObservaciones(), Auditoria, UnidadDeTrabajo, Reloj);

    public ObtenerRiesgoPaciente ObtenerRiesgoPaciente() => new(Pacientes, Hospitalizaciones, Historial, Historial, PresentadorRiesgo());

    public CambiarEscalaSpo2 CambiarEscalaSpo2() =>
        new(new CambiarEscalaSpo2Validador(), Pacientes, Hospitalizaciones, Dispositivos, Auditoria, UnidadDeTrabajo, Resolutor,
            Evaluador(), Historial, ObtenerRiesgoPaciente(), PublicadorRiesgo(), Reloj);

    public RegistrarPresenciaDispositivo RegistrarPresencia() => new(Presencia, SincronizarPantalla(), Reloj);

    // ---- Fase 6: alertas, turnos y notificaciones.
    public ContextoHospitalizaciones Contextos() => new(Hospitalizaciones);

    public TurnoVigenteEnMemoria TurnoVigente() => new(Programacion, Usuarios, Catalogo);

    public DestinatariosAlertas Destinatarios() => new(TurnoVigente(), Usuarios, Reloj);

    public NotificadorAlertas Notificador() => new(TiempoReal, Push, Moviles, UnidadDeTrabajo, Bitacora, Reloj);

    public ServicioAlertas ServicioAlertas() => new(Alertas, Contextos(), Destinatarios(), Notificador(), UnidadDeTrabajo, Bitacora, OpcionesAlertas, Reloj);

    public PublicadorRiesgoEvaluado PublicadorRiesgo() =>
        new([new AlertaDeRiesgoAlEvaluar(ServicioAlertas()), new PublicarRiesgoEnTiempoReal(Contextos(), TiempoReal)], Bitacora);

    public AccesoServicio Acceso() => new(Usuarios);

    public VigilarAlertas VigilarAlertas() =>
        new(Hospitalizaciones, Historial, Historial, Alertas, Servicios, Programacion, Presencia, ServicioAlertas(), Notificador(), Contextos(),
            Catalogo, OpcionesAlertas, Clinicas, Reloj);

    public TomarAlerta TomarAlerta() => new(Alertas, Acceso(), Contextos(), Notificador(), Auditoria, UnidadDeTrabajo, Reloj);

    public AtenderAlerta AtenderAlerta() =>
        new(new AtenderAlertaValidador(), Alertas, Acceso(), Contextos(), Notificador(), Auditoria, UnidadDeTrabajo, Reloj, EjecucionesPlan(), NotificadorPlan());

    public ListarAlertas ListarAlertas() => new(new ListarAlertasValidador(), Alertas, Acceso());

    public ObtenerProgramacion ObtenerProgramacion() => new(Servicios, Usuarios, Programacion);

    public CargarProgramacion CargarProgramacion() =>
        new(Servicios, Usuarios, Programacion, Auditoria, UnidadDeTrabajo, ObtenerProgramacion(), Catalogo, Reloj);

    public ObtenerTurnoActual ObtenerTurnoActual() => new(Servicios, Usuarios, Programacion, TurnoVigente(), Catalogo, Reloj);

    public ObtenerEntregaTurno ObtenerEntregaTurno() =>
        new(Servicios, Hospitalizaciones, Historial, Historial, Historial, Alertas, Presencia, Acceso(), Catalogo,
            OpcionesTelemetria.PorDefecto, Clinicas, Reloj, new PlanEnEntregaTurno(Plan, CatalogoNnn));

    public ListarIncidencias ListarIncidencias() => new(new ListarIncidenciasValidador(), Historial);

    // ---- Fase 7: historial del paciente, notas y auditoría.
    public EpisodiosPaciente Episodios() => new(Pacientes, Hospitalizaciones);

    public AuditoriaHistorial AuditoriaHistorial() => new(Auditoria, UnidadDeTrabajo, Reloj);

    public DirectorioProfesionales Directorio() => new(Usuarios);

    public PresentadorNotas PresentadorNotas() => new(Directorio());

    public ListarHospitalizacionesPaciente ListarHospitalizaciones() => new(Pacientes, Hospitalizaciones, AuditoriaHistorial());

    public OpcionesHistorial OpcionesHistorial { get; set; } = OpcionesHistorial.PorDefecto;

    public ResumirLecturasAntiguas ResumirLecturasAntiguas() => new(Historial, Hospitalizaciones, OpcionesHistorial, Bitacora, Reloj);

    public ObtenerHistorialRiesgo ObtenerHistorialRiesgo() => new(Episodios(), Historial, AuditoriaHistorial(), Reloj);

    public ListarObservacionesPaciente ListarObservaciones() => new(Episodios(), Historial, Directorio(), AuditoriaHistorial(), Reloj);

    public ObtenerResumenPaciente ObtenerResumen() =>
        new(Episodios(), Hospitalizaciones, Historial, Historial, Historial, Alertas, Notas, PresentadorRiesgo(), PresentadorNotas(),
            AuditoriaHistorial(), Clinicas, Reloj, new ResumenPlanCuidados(Plan, CatalogoNnn, ServicioSugerencias()));

    public ListarAlertasPaciente ListarAlertasPaciente() => new(Episodios(), Alertas, AuditoriaHistorial());

    public RegistrarNota RegistrarNota() =>
        new(new RegistrarNotaValidador(), Pacientes, Hospitalizaciones, Notas, PresentadorNotas(), Auditoria, UnidadDeTrabajo, Reloj);

    public ListarNotas ListarNotas() => new(Episodios(), Notas, PresentadorNotas(), AuditoriaHistorial());

    public EditarNota EditarNota() => new(new EditarNotaValidador(), Notas, PresentadorNotas(), Auditoria, UnidadDeTrabajo, Reloj);

    public ListarVersionesNota ListarVersionesNota() => new(Notas, AuditoriaHistorial());

    public ListarAuditoria ListarAuditoria() => new(new ListarAuditoriaValidador(), Auditoria);

    // ---- Fase 8: proceso de enfermería NNN.
    public NotificadorPlanCuidados NotificadorPlan() => new(Contextos(), TiempoReal, Bitacora);

    public ContextoPlanCuidados ContextoPlan() => new(Pacientes, Hospitalizaciones, Alertas, Usuarios);

    public EjecucionesPlan EjecucionesPlan() => new(Plan, ContextoPlan(), Auditoria);

    public ServicioSugerenciasNnn ServicioSugerencias() =>
        new(Historial, Historial, Alertas, Plan, UnidadDeTrabajo, Bitacora, Catalogo, Clinicas, Reloj);

    /// <summary>Lectura del contrato con marca de tiempo relativa a la hora actual del reloj falso.</summary>
    public LecturaTelemetria Lectura(Domain.Telemetria.SignosMedidos signos, string dispositivo = "ESP32-001", int segundos = 0, bool caida = false) =>
        new(dispositivo, Reloj.AhoraUtc.AddSeconds(segundos), 1, signos, caida, Domain.Telemetria.OrigenLectura.ApiDesarrollo);

    public ListarPacientesHospitalizados ListarPacientesHospitalizados() =>
        new(new ListarPacientesHospitalizadosValidador(), Hospitalizaciones, Reloj);

    public ObtenerPaciente ObtenerPaciente() => new(Pacientes, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Reloj);

    public ObtenerHospitalizacionActivaPorDispositivo ObtenerHospitalizacionActivaPorDispositivo() => new(Hospitalizaciones);

    public Cama AgregarCama(string codigo = "MED-B-01")
    {
        var cama = Cama.Registrar(codigo, Servicio.Id);
        Camas.Agregar(cama);
        return cama;
    }

    public Dispositivo AgregarDispositivo(string codigo = "ESP32-001")
    {
        var dispositivo = Dispositivo.Registrar(codigo, null);
        Dispositivos.Agregar(dispositivo);
        return dispositivo;
    }

    public RegistrarIngresoPacienteComando ComandoIngreso(
        Guid? camaId, string documento = "12345678", string nombre = "Rosa Huamán Quispe",
        string? fechaNacimiento = "1958-03-14", int? edad = null) =>
        new(nombre, "Dni", documento, fechaNacimiento, edad, camaId, "Neumonía adquirida en la comunidad", EnfermeraId, "10.0.0.7");

    /// <summary>
    /// Corre el arrange como si fuera hace <paramref name="hace"/> (por ejemplo, vincular el sensor antes de enviar lecturas
    /// del búfer: la ingesta rechaza lo medido antes de la vinculación vigente).
    /// </summary>
    public async Task<T> HaceAsync<T>(TimeSpan hace, Func<Task<T>> accion)
    {
        Reloj.Avanzar(-hace);
        try
        {
            return await accion();
        }
        finally
        {
            Reloj.Avanzar(hace);
        }
    }

    /// <summary>Ingresa un paciente en una cama nueva y devuelve su ficha.</summary>
    public async Task<FichaPacienteDto> IngresarAsync(string codigoCama = "MED-B-01", string documento = "12345678")
    {
        var cama = Camas.Camas.SingleOrDefault(c => c.Codigo == codigoCama) ?? AgregarCama(codigoCama);
        var resultado = await RegistrarIngresoPaciente().EjecutarAsync(ComandoIngreso(cama.Id, documento));
        Assert.True(resultado.EsExito, resultado.EsExito ? null : resultado.Error.Mensaje);
        return resultado.Valor;
    }

    public async Task<VinculacionDto> VincularAsync(Guid pacienteId, Dispositivo dispositivo)
    {
        var resultado = await VincularDispositivo().EjecutarAsync(new VincularDispositivoComando(pacienteId, dispositivo.Id, EnfermeraId, null));
        Assert.True(resultado.EsExito, resultado.EsExito ? null : resultado.Error.Mensaje);
        return resultado.Valor;
    }

    public Usuario AgregarUsuario(string correo, string contrasena, Rol rol = Rol.Enfermera, string nombre = "Usuario de prueba", Guid? servicioId = null)
    {
        var usuario = Domain.Usuarios.Usuario.Crear(nombre, Correo.Crear(correo), Hasher.Calcular(contrasena), rol, Reloj.AhoraUtc,
            rol == Rol.Administrador ? null : servicioId ?? Servicio.Id);
        Usuarios.Agregar(usuario);
        return usuario;
    }

    public async Task<SesionIniciada> IniciarSesionAsync(string correo, string contrasena)
    {
        var resultado = await IniciarSesion().EjecutarAsync(new IniciarSesionComando(correo, contrasena, "10.0.0.1"));
        Assert.True(resultado.EsExito, resultado.EsExito ? null : resultado.Error.Mensaje);
        return resultado.Valor;
    }
}
