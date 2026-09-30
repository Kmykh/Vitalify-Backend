namespace Vitalify.Domain.Telemetria;

public enum VariableSigno
{
    Fc = 1,
    Fr = 2,
    Spo2 = 3,
    Temperatura = 4,
    Pas = 5,
    Pad = 6,
    Bateria = 7,
}

/// <summary>Adaptador de entrada que envió la lectura. Solo sirve para trazabilidad: el núcleo no se comporta distinto.</summary>
public enum OrigenLectura
{
    Simulador = 1,
    ApiDesarrollo = 2,
    Mqtt = 3,
}

public enum TipoIncidencia
{
    FueraDeRango = 1,
    PresionIncompleta = 2,
    DispositivoDesconocido = 3,
    DispositivoSinVincular = 4,
    MarcaDeTiempoInvalida = 5,
}

/// <summary>Estado de una variable al leerla: se calcula con la hora actual, no se guarda.</summary>
public enum EstadoVariable
{
    SinDatos = 1,
    Vigente = 2,
    PendienteActualizacion = 3,
}

/// <summary>Estado de la señal del sensor al leerla.</summary>
public enum EstadoSenal
{
    SinDatos = 1,
    ConDatos = 2,
    SinSenal = 3,
}
