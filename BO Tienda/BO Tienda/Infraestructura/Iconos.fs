// =====================================================================
// Infraestructura/Iconos.fs — Iconos de trazo y logotipo (SVG en línea)
// ---------------------------------------------------------------------
// Los iconos toman el color del texto (currentColor) y el logotipo el de
// sus variables CSS: no llevan colores escritos a mano. Se pintan en las
// vistas con @Iconos.Reloj, etc. El tamaño y el trazo están en site.css
// (.icono). Nada de emojis.
// =====================================================================

module BO_Tienda.Infraestructura.Iconos

open Microsoft.AspNetCore.Html

let private icono (trazos: string) : IHtmlContent =
    HtmlString($"""<svg class="icono" viewBox="0 0 24 24" aria-hidden="true" focusable="false">{trazos}</svg>""")

// ----- Tarjetas de indicador -----
let Diana = icono "<circle cx='11' cy='13' r='8'/><circle cx='11' cy='13' r='4'/><path d='m11 13 9-9M16 4h4v4'/>"
let Ticket = icono "<rect x='3' y='6' width='18' height='12' rx='2'/><path d='M7 10h6M7 14h10'/>"
let Reloj = icono "<circle cx='12' cy='13' r='8'/><path d='M12 9v4l2.5 2.5M5 4 2.5 6.5M19 4l2.5 2.5'/>"
let Mensaje = icono "<path d='M4 5h16v11H9l-5 4z'/><path d='M8 9h8M8 12h5'/>"
let Check = icono "<circle cx='12' cy='12' r='9'/><path d='m8 12.5 3 3 5-6'/>"
let Bandeja = icono "<path d='M4 13h4l2 3h4l2-3h4'/><path d='M6 5h12l2 8v6H4v-6z'/>"
let Telefono = icono "<path d='M5 4h3l2 5-2.5 1.5a11 11 0 0 0 5 5L14 13l5 2v3a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2'/>"
let Repetir = icono "<path d='m17 2 3 3-3 3'/><path d='M4 11V9a4 4 0 0 1 4-4h12'/><path d='m7 22-3-3 3-3'/><path d='M20 13v2a4 4 0 0 1-4 4H4'/>"
let Desvio = icono "<circle cx='6' cy='6' r='2'/><circle cx='6' cy='18' r='2'/><circle cx='18' cy='8' r='2'/><path d='M6 8v8M18 10a6 6 0 0 1-6 6H8'/>"
let Capas = icono "<path d='m12 3 9 5-9 5-9-5z'/><path d='m3 13 9 5 9-5'/>"
let Persona = icono "<circle cx='9' cy='8' r='4'/><path d='M2 21a7 7 0 0 1 14 0M19 8v6M16 11h6'/>"
let Bandera = icono "<path d='M5 21V4'/><path d='M5 4h12l-2 4 2 4H5'/>"

// ----- Acciones y avisos -----
let Actualizar = icono "<path d='M20 11a8 8 0 0 0-14.3-4.9L4 8M4 4v4h4M4 13a8 8 0 0 0 14.3 4.9L20 16M20 20v-4h-4'/>"
let Descarga = icono "<path d='M12 4v11M7 10l5 5 5-5M5 20h14'/>"
let Flecha = icono "<path d='m9 6 6 6-6 6'/>"
let Cerrar = icono "<path d='m6 6 12 12M18 6 6 18'/>"
let Candado = icono "<rect x='5' y='11' width='14' height='9' rx='2'/><path d='M8 11V8a4 4 0 0 1 8 0v3'/>"
let Alerta = icono "<path d='M12 4 3 20h18z'/><path d='M12 10v4M12 17h.01'/>"
let Info = icono "<circle cx='12' cy='12' r='9'/><path d='M12 11v5M12 8h.01'/>"

// ----- Tema: automático (como Windows), claro y oscuro -----
let TemaAuto = icono "<rect x='3' y='4' width='18' height='12' rx='2'/><path d='M8 20h8M12 16v4'/>"
let Sol = icono "<circle cx='12' cy='12' r='4'/><path d='M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4'/>"
let Luna = icono "<path d='M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z'/>"

/// Logotipo de «Informes BI»: tres barras con los colores de --logo-suave
/// y --logo-fuerte (marino; claro en el tema oscuro).
let Logotipo: IHtmlContent =
    HtmlString(
        "<svg class=\"logotipo\" viewBox=\"0 0 24 22\" aria-hidden=\"true\" focusable=\"false\">"
        + "<rect class=\"logotipo-barra\" x=\"0\" y=\"12\" width=\"6\" height=\"10\" rx=\"2\"/>"
        + "<rect class=\"logotipo-barra\" x=\"9\" y=\"6\" width=\"6\" height=\"16\" rx=\"2\"/>"
        + "<rect class=\"logotipo-barra fuerte\" x=\"18\" y=\"0\" width=\"6\" height=\"22\" rx=\"2\"/>"
        + "</svg>"
    )
