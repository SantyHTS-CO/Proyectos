// =====================================================================
// Services/BigQueryOdbcFuente.fs — Lee datos de Google BigQuery por ODBC
// ---------------------------------------------------------------------
// Paquete NuGet: System.Data.Odbc
// Driver: "Simba ODBC Driver for Google BigQuery" (64 bits), instalado en
// la máquina donde corre la app (tu PC y, al publicar, el servidor).
//
// La conexión sale de "ConnectionStrings:BigQueryOdbc". Lo más simple es
// apuntar a un DSN ya configurado en "Orígenes de datos ODBC (64 bits)":
//     "DSN=BQCOL"
// El DSN guarda el proyecto de facturación (Catalog) y el login de Google,
// así no hay llaves ni contraseñas en el código.
//
// ODBC usa parámetros POSICIONALES: si el SQL tiene "?", se llena con la
// fecha de hoy.
// =====================================================================

namespace BO_Tienda.Services

open System
open System.Data.Common
open System.Data.Odbc
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

type BigQueryOdbcFuente(config: IConfiguration, log: ILogger<BigQueryOdbcFuente>) =

    // No lanzamos error aquí: si falta la cadena, el informe muestra el
    // error en rojo y la portada sigue funcionando.
    let cadena = config.GetConnectionString "BigQueryOdbc"

    let maxIntentos = 3

    // Errores pasajeros de red: vale la pena reintentar. Un error de SQL o de
    // permisos ("Access Denied") NO entra aquí y se muestra de inmediato.
    let esCorteDeRed (ex: OdbcException) =
        [ "Failure when receiving data"; "Couldn't connect"; "Timeout was reached" ]
        |> List.exists (fun texto -> ex.Message.Contains(texto, StringComparison.OrdinalIgnoreCase))

    /// Abre la conexión, ejecuta la consulta y entrega el lector a "leer".
    /// Si BigQuery corta la conexión a mitad (pasa de vez en cuando por la
    /// red o el proxy), lo reintenta hasta maxIntentos antes de mostrar el error.
    let ejecutarAsync (consulta: string) (fecha: DateOnly) (leer: DbDataReader -> Task<'T>) (ct: CancellationToken) =
        let rec intentar intento : Task<'T> =
            task {
                // Ok = datos; Error = corte de red que vale la pena reintentar.
                // Cualquier otro error sube tal cual.
                let! resultado =
                    task {
                        try
                            log.LogInformation("BigQuery (ODBC): ejecutando consulta para {Fecha} (intento {Intento})", fecha, intento)

                            // Ojo: en ODBC los métodos "Async" corren en realidad de forma
                            // síncrona (el driver no tiene API asíncrona). Funciona igual y,
                            // como el resultado queda en caché, es una consulta por informe y día.
                            use conexion = new OdbcConnection(cadena)
                            do! conexion.OpenAsync ct

                            use comando = new OdbcCommand(consulta, conexion, CommandTimeout = 300) // segundos; igual al Timeout del DSN

                            // Parámetro posicional: llena el "?" del SQL. El nombre es solo
                            // informativo; en ODBC cuenta el ORDEN en que se agregan.
                            if consulta.Contains '?' then
                                comando.Parameters.Add("fecha", OdbcType.Date).Value <- box (fecha.ToDateTime TimeOnly.MinValue)

                            use! lector = comando.ExecuteReaderAsync ct
                            let! datos = leer lector
                            return Ok datos
                        with
                        | :? OdbcException as ex when intento < maxIntentos && esCorteDeRed ex ->
                            return Error ex
                    }

                match resultado with
                | Ok datos -> return datos
                | Error ex ->
                    // Espera un poco más en cada intento (2 s, 4 s) para dar
                    // tiempo a que la red/proxy se recupere.
                    log.LogWarning("BigQuery (ODBC): corte de red, reintentando… {Mensaje}", ex.Message)
                    do! Task.Delay(TimeSpan.FromSeconds(2.0 * float intento), ct)
                    return! intentar (intento + 1)
            }

        task {
            if String.IsNullOrWhiteSpace cadena then
                invalidOp "Falta ConnectionStrings:BigQueryOdbc en appsettings.json"
            return! intentar 1
        }

    interface IFuenteDatos with
        member _.Nombre = "BigQuery"

        /// Ejecuta la consulta y devuelve todas sus columnas tal cual.
        member _.ObtenerTablaAsync(consulta, fecha, ct) =
            ejecutarAsync consulta fecha (fun lector -> LectorTabla.leerAsync lector ct) ct
