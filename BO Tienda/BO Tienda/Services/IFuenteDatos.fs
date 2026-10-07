// =====================================================================
// Services/IFuenteDatos.fs — El "contrato" de las fuentes de datos
// ---------------------------------------------------------------------
// Una interfaz dice QUÉ se hace, no CÓMO. Hoy la única fuente es BigQuery
// (BigQueryOdbcFuente). Si mañana quieres leer de SQL Server o de una API,
// creas otro tipo que implemente esto, lo registras en Program.fs y en
// appsettings pones "Fuente": "<su Nombre>".
// =====================================================================

namespace BO_Tienda.Services

open System
open System.Data.Common
open System.Threading
open System.Threading.Tasks
open BO_Tienda.Models

type IFuenteDatos =
    /// Nombre con el que se identifica en appsettings ("BigQuery").
    abstract Nombre: string

    /// Ejecuta la consulta y devuelve TODAS sus columnas tal cual.
    /// consulta = el SQL del informe; fecha = hoy, para el "?" del SQL (si lo tiene).
    abstract ObtenerTablaAsync: consulta: string * fecha: DateOnly * ct: CancellationToken -> Task<TablaDatos>

/// Convierte cualquier lector ADO.NET (ODBC, SQL Server…) en una TablaDatos.
module internal LectorTabla =

    let leerAsync (lector: DbDataReader) (ct: CancellationToken) =
        task {
            let tabla = TablaDatos()
            for i in 0 .. lector.FieldCount - 1 do
                tabla.Columnas.Add(lector.GetName i)

            let! hayFila = lector.ReadAsync ct
            let mutable hayFila = hayFila
            while hayFila do
                let valores = Array.zeroCreate<obj> lector.FieldCount
                lector.GetValues valores |> ignore
                // DBNull -> null, para no arrastrar el "nulo de base de datos" a la vista
                tabla.Filas.Add(valores |> Array.map (fun v -> if v :? DBNull then null else v))
                let! siguiente = lector.ReadAsync ct
                hayFila <- siguiente
            return tabla
        }

/// Ayudas para convertir valores "sueltos" (obj, DBNull) a número o
/// texto sin que la app explote por un nulo.
module internal Convertir =

    let entero (v: obj) =
        match v with
        | null
        | :? DBNull -> 0
        | v -> Convert.ToInt32 v

    let texto (v: obj) =
        match v with
        | null
        | :? DBNull -> ""
        | v -> (string v).Trim()
