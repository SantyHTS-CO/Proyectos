// =====================================================================
// Controllers/InformesController.fs — Muestra un informe (sus pestañas)
// ---------------------------------------------------------------------
// Rutas:
//   GET  /Informes/Ver/bo-tiendas?Agente=…        -> pestaña Tickets (usa caché)
//   GET  /Informes/Motivo/bo-tiendas?…            -> pestaña Motivo (usa caché)
//   POST /Informes/Actualizar/bo-tiendas          -> fuerza recarga de la pestaña y redirige
//   GET  /Informes/Descargar/bo-tiendas?…         -> Excel de los tickets filtrados
//   GET  /Informes/DescargarMotivo/bo-tiendas?…   -> Excel de las llamadas filtradas
// =====================================================================

namespace BO_Tienda.Controllers

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open BO_Tienda.Models
open BO_Tienda.Services

type InformesController(informes: InformeService) =
    inherit Controller()

    let tipoExcel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"

    let nombreExcel (cfg: InformeConfig) (pagina: string) =
        $"""{cfg.Id}_{pagina}_{DateTime.Now.ToString "yyyyMMdd_HHmm"}.xlsx"""

    /// Un informe sin permiso vuelve a la portada con el aviso
    /// "Debes solicitar acceso con tu administrador".
    member private this.SinAcceso(cfg: InformeConfig) : IActionResult =
        this.TempData["Aviso"] <- $"No tienes permiso para entrar a {cfg.Titulo}."
        this.RedirectToAction("Index", "Home")

    // task { } + Task<IActionResult>: la petición no bloquea un hilo mientras
    // BigQuery responde. El CancellationToken se cancela si el usuario
    // cierra la pestaña, así no seguimos consultando en vano.
    // "filtros" lo llena ASP.NET desde la URL.
    member this.Ver(id: string, filtros: FiltrosTickets, ct: CancellationToken) : Task<IActionResult> =
        task {
            match informes.Buscar id with
            | None -> return this.NotFound() :> IActionResult
            | Some cfg when not cfg.Habilitado -> return this.SinAcceso cfg
            | Some cfg ->
                let! cargado = informes.ObtenerTablaAsync(cfg, false, ct)
                return this.View("Tickets", TableroTickets.construir cfg cargado filtros) :> IActionResult // Views/Informes/Tickets.cshtml
        }

    // Pestaña Motivo: usa su consulta y la de Tickets (para saber el agente
    // de cada ticket y los tickets del periodo). Las dos salen de la caché.
    member this.Motivo(id: string, filtros: FiltrosMotivo, ct: CancellationToken) : Task<IActionResult> =
        task {
            match informes.Buscar id with
            | Some cfg when not cfg.Habilitado -> return this.SinAcceso cfg
            | Some cfg when cfg.TieneMotivo ->
                let! tickets = informes.ObtenerTablaAsync(cfg, false, ct)
                let! llamadas = informes.ObtenerMotivoAsync(cfg, false, ct)
                return this.View("Motivo", TableroMotivo.construir cfg tickets llamadas filtros) :> IActionResult
            | _ -> return this.NotFound() :> IActionResult
        }

    // POST + token antifalsificación: evita que otra web dispare recargas.
    // "pagina" = la pestaña desde la que se pulsó (tickets | motivo).
    // "filtros" = la parte ?… de la URL, para volver con los mismos filtros.
    [<HttpPost; ValidateAntiForgeryToken>]
    member this.Actualizar(id: string, pagina: string, filtros: string, ct: CancellationToken) : Task<IActionResult> =
        task {
            match informes.Buscar id with
            | Some cfg when cfg.Habilitado ->
                let enMotivo = pagina = "motivo" && cfg.TieneMotivo

                let! _ =
                    if enMotivo then informes.ObtenerMotivoAsync(cfg, true, ct)
                    else informes.ObtenerTablaAsync(cfg, true, ct)

                this.TempData["Ok"] <- $"{cfg.Titulo} actualizado."

                // Patrón PRG (Post-Redirect-Get): al recargar el navegador no
                // vuelve a enviar el POST. LocalRedirect impide saltar a otra web.
                let accion = if enMotivo then nameof this.Motivo else nameof this.Ver
                let consulta = if not (isNull filtros) && filtros.StartsWith '?' then filtros else ""
                let url = this.Url.Action(accion, {| id = id |}) + consulta
                return this.LocalRedirect url :> IActionResult
            | _ -> return this.NotFound() :> IActionResult
        }

    // Botón "Descarga general" de Tickets: usa los datos en caché y los mismos filtros.
    member this.Descargar(id: string, filtros: FiltrosTickets, ct: CancellationToken) : Task<IActionResult> =
        task {
            match informes.Buscar id with
            | Some cfg when cfg.Habilitado ->
                match! informes.ObtenerTablaAsync(cfg, false, ct) with
                | Error _ -> return this.RedirectToAction(nameof this.Ver, {| id = id |}) :> IActionResult // allí se ve el error
                | Ok datos as cargado ->
                    let tablero = TableroTickets.construir cfg cargado filtros
                    let excel = TableroTickets.exportarExcel datos.Datos tablero.FilasDetalle
                    return this.File(excel, tipoExcel, nombreExcel cfg "tickets") :> IActionResult
            | _ -> return this.NotFound() :> IActionResult
        }

    // Botón "Descarga general" de Motivo.
    member this.DescargarMotivo(id: string, filtros: FiltrosMotivo, ct: CancellationToken) : Task<IActionResult> =
        task {
            match informes.Buscar id with
            | Some cfg when cfg.Habilitado && cfg.TieneMotivo ->
                let! tickets = informes.ObtenerTablaAsync(cfg, false, ct)

                match! informes.ObtenerMotivoAsync(cfg, false, ct) with
                | Ok datos as llamadas ->
                    let pagina = TableroMotivo.construir cfg tickets llamadas filtros
                    let excel = TableroMotivo.exportarExcel datos.Datos pagina.FilasDetalle
                    return this.File(excel, tipoExcel, nombreExcel cfg "motivo") :> IActionResult
                | Error _ -> return this.RedirectToAction(nameof this.Motivo, {| id = id |}) :> IActionResult
            | _ -> return this.NotFound() :> IActionResult
        }
