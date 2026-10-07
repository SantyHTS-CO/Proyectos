// =====================================================================
// Services/InformeService.fs — Orquestador: catálogo + fuente + caché
// ---------------------------------------------------------------------
// Responsabilidades:
//  1. Leer el catálogo de informes desde appsettings.json
//  2. Elegir la fuente de cada informe (hoy: BigQuery)
//  3. Guardar el resultado en caché N horas ("CacheHoras"), así no se
//     consulta BigQuery cada vez que alguien abre la página
//  4. Permitir forzar la recarga (botón "Actualizar")
//  5. Leer la consulta desde un archivo (Consultas/*.bqsql)
// =====================================================================

namespace BO_Tienda.Services

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Configuration
open BO_Tienda.Models

// .NET inyecta TODAS las implementaciones registradas de IFuenteDatos
// cuando pides IEnumerable<IFuenteDatos>.
type InformeService(fuentes: IEnumerable<IFuenteDatos>, config: IConfiguration, cache: IMemoryCache,
                    env: IWebHostEnvironment) =

    let fuentes = List.ofSeq fuentes

    let informes =
        match config.GetSection("Informes").Get<InformeConfig[]>() with
        | null -> [||]
        | lista -> lista

    let duracionCache = TimeSpan.FromHours(float (config.GetValue<int>("CacheHoras", 12)))
    let raiz = env.ContentRootPath   // carpeta del proyecto (para Consultas/*.bqsql)

    let mismoNombre (a: string) (b: string) = a.Equals(b, StringComparison.OrdinalIgnoreCase)

    // Extensiones de archivo de consulta. ".bqsql" = SQL de BigQuery: Visual
    // Studio no lo revisa como SQL Server, así no marca errores falsos.
    let extensionesConsulta = [ ".bqsql"; ".sql" ]

    /// "Consultas/bo_tiendas.bqsql" -> lee el archivo (así el SQL puede tener
    /// varias líneas y comentarios). Se lee en cada carga: si lo editas,
    /// basta con pulsar "Actualizar". Cualquier otra cosa se usa tal cual.
    let leerConsulta (consulta: string) =
        let esArchivo =
            extensionesConsulta |> List.exists (fun e -> consulta.EndsWith(e, StringComparison.OrdinalIgnoreCase))
        if not esArchivo then
            consulta
        else
            let ruta = Path.Combine(raiz, consulta)
            if not (File.Exists ruta) then
                raise (FileNotFoundException $"No se encontró el archivo de consulta: {ruta}")
            File.ReadAllText ruta

    /// Devuelve la tabla de una consulta del informe (desde caché si ya se
    /// cargó hoy) o el mensaje de error, para mostrarlo en la página sin tumbarla.
    /// pagina = tickets | motivo (una entrada de caché por cada una).
    /// forzar = true: ignora la caché y vuelve a consultar.
    let obtener (cfg: InformeConfig) (pagina: string) (archivoConsulta: string) (forzar: bool) (ct: CancellationToken) =
        task {
            let fecha = DateOnly.FromDateTime DateTime.Now
            let llave = $"""informe:{cfg.Id}:{pagina}:{fecha.ToString "yyyyMMdd"}"""   // una entrada por informe, página y día

            if forzar then cache.Remove llave

            try
                // GetOrCreateAsync: si la llave existe devuelve lo guardado;
                // si no, ejecuta la función, guarda el resultado y lo devuelve.
                // Si la consulta falla, no se guarda nada y el error sube.
                let! resultado =
                    cache.GetOrCreateAsync<Cargado<TablaDatos>>(llave, fun entrada ->
                        task {
                            entrada.AbsoluteExpirationRelativeToNow <- Nullable duracionCache

                            let fuente =
                                match fuentes |> List.tryFind (fun f -> mismoNombre f.Nombre cfg.Fuente) with
                                | Some f -> f
                                | None -> invalidOp $"Fuente desconocida: {cfg.Fuente}"

                            let consulta = leerConsulta archivoConsulta
                            let reloj = Stopwatch.StartNew()   // medimos cuánto tarda
                            let! datos = fuente.ObtenerTablaAsync(consulta, fecha, ct)
                            reloj.Stop()

                            return { Datos = datos; CargadoEn = DateTime.Now; Duracion = reloj.Elapsed }
                        })
                return Ok resultado
            with ex ->
                return Error ex.Message
        }

    /// Lista de informes para pintar las tarjetas de la portada.
    member _.Catalogo = informes :> IReadOnlyList<InformeConfig>

    member _.Buscar(id: string) =
        informes |> Array.tryFind (fun i -> mismoNombre i.Id id)

    /// Tickets del informe (pestaña «Tickets»; «Motivo» también la usa).
    member _.ObtenerTablaAsync(cfg: InformeConfig, forzar: bool, ct: CancellationToken) : Task<Result<Cargado<TablaDatos>, string>> =
        obtener cfg "tickets" cfg.Consulta forzar ct

    /// Llamadas tras el ticket y su motivo (pestaña «Motivo», ConsultaMotivo).
    member _.ObtenerMotivoAsync(cfg: InformeConfig, forzar: bool, ct: CancellationToken) : Task<Result<Cargado<TablaDatos>, string>> =
        obtener cfg "motivo" cfg.ConsultaMotivo forzar ct
