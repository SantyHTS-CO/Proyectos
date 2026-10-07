// =====================================================================
// Controllers/HomeController.fs — Portada: "Informes"
// ---------------------------------------------------------------------
//   GET /  -> tarjetas de los informes de appsettings.json (BO Tiendas)
// =====================================================================

namespace BO_Tienda.Controllers

open System.Diagnostics
open Microsoft.AspNetCore.Mvc
open BO_Tienda.Models
open BO_Tienda.Services

type HomeController(informes: InformeService) =
    inherit Controller()

    // GET /  ó  /Home/Index
    member this.Index() = this.View(informes.Catalogo)

    // Página de error que ya trae la plantilla
    [<ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)>]
    member this.Error() =
        let reqId =
            if isNull Activity.Current then
                this.HttpContext.TraceIdentifier
            else
                Activity.Current.Id

        this.View({ RequestId = reqId })
