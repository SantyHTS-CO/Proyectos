// =====================================================================
// Program.fs  —  Punto de entrada de la aplicación "Informes BI"
// ---------------------------------------------------------------------
// Aquí se "arma" la aplicación: se registran los servicios (inyección de
// dependencias), la caché, y el pipeline HTTP (rutas, archivos estáticos).
// =====================================================================

namespace BO_Tienda

#nowarn "20"

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open BO_Tienda.Services

module Program =
    let exitCode = 0

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)

        // -----------------------------------------------------------------
        // 1) MVC: controladores + vistas Razor (.cshtml). En un proyecto F#
        //    las vistas se compilan al arrancar (RuntimeCompilation), por eso
        //    al editar un .cshtml basta con recargar la página.
        // -----------------------------------------------------------------
        builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation()

        // -----------------------------------------------------------------
        // 2) Caché en memoria. Es lo que permite que, como en GAIA, cada informe
        //    guarde lo que trajo de BigQuery durante X horas y no consulte la
        //    base cada vez que alguien abre la página.
        // -----------------------------------------------------------------
        builder.Services.AddMemoryCache()

        // -----------------------------------------------------------------
        // 3) Fuente de datos: BigQuery por ODBC (DSN del driver Simba).
        //    Implementa IFuenteDatos; si algún día hay otra fuente, se registra
        //    aquí igual y en appsettings se elige con "Fuente".
        //
        //    AddSingleton  -> una sola instancia para toda la app (solo guarda
        //                     la configuración; la conexión se abre por consulta).
        // -----------------------------------------------------------------
        builder.Services.AddSingleton<IFuenteDatos, BigQueryOdbcFuente>()

        // Servicio que decide qué fuente usar por informe y maneja la caché.
        builder.Services.AddSingleton<InformeService>()

        let app = builder.Build()

        // -----------------------------------------------------------------
        // 4) Pipeline HTTP (el orden importa)
        // -----------------------------------------------------------------
        if not (app.Environment.IsDevelopment()) then
            app.UseExceptionHandler("/Home/Error") // página amigable de error
            app.UseHsts() |> ignore                // fuerza HTTPS en navegadores

        app.UseHttpsRedirection() // http -> https
        app.UseStaticFiles()      // sirve wwwroot (css, imágenes)
        app.UseRouting()
        app.UseAuthorization()    // más adelante: login / roles (ver README)

        // Ruta por defecto: /{controlador}/{acción}/{id?}
        // Sin nada en la URL -> HomeController.Index (portada "Informes")
        app.MapControllerRoute(name = "default", pattern = "{controller=Home}/{action=Index}/{id?}")

        app.Run()

        exitCode
