// La configuración de cada FabricaVitalify (cadena del contenedor, etc.) se pasa como variables de entorno del
// proceso, así que las colecciones no pueden arrancar en paralelo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
