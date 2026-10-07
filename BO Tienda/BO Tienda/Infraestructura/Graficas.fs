// =====================================================================
// Infraestructura/Graficas.fs — Gráfica por tiempo en SVG (servidor)
// ---------------------------------------------------------------------
// Guía de estilos (5): las gráficas se generan en el servidor como SVG y
// cada punto lleva un <title> «Etiqueta · Serie valor · …»; site.js lo
// convierte en la ficha al pasar el ratón, con la guía vertical.
// Dos series (principal y secundaria), con las cifras en pastillas.
// Aquí solo se calculan posiciones y trazos; el marcado está en
// Views/Shared/_GraficaLineas.cshtml y los colores en site.css.
// =====================================================================

module BO_Tienda.Infraestructura.Graficas

open System
open BO_Tienda.Models

/// Una serie: nombre (leyenda y ficha), un valor por periodo y cómo se escribe.
type Serie =
    { Nombre: string
      Valores: float[]
      Escribir: float -> string }

let serie nombre valores escribir =
    { Nombre = nombre; Valores = valores; Escribir = escribir }

/// Escala del eje vertical.
type Escala =
    | EscalaPropia      // cada línea a su máximo (volúmenes distintos, como en Power BI)
    | EscalaPorcentaje  // las dos en la misma escala de % (0 a un tope redondo), con rejilla

// Alto del SVG y márgenes (px): arriba, las pastillas de cifras; abajo,
// pastillas y eje. site.css (.guia) usa los mismos: top = arriba,
// height = alto - arriba - abajo.
let private alto = 270.0
let private arriba = 30.0
let private abajo = 44.0
let private pastilla = 16.0   // distancia del punto al centro de su pastilla

let private n = Formato.Invariante

/// Línea suave que pasa por los puntos (x de 0 a 1000, y en px).
let private curva (p: (float * float)[]) =
    let x0, y0 = p[0]
    let tramos =
        [ for i in 1 .. p.Length - 1 do
              let xa, ya = p[i - 1]
              let xb, yb = p[i]
              let xm = (xa + xb) / 2.0
              $" C{n xm},{n ya} {n xm},{n yb} {n xb},{n yb}" ]
    $"M{n x0},{n y0}" + String.concat "" tramos

/// La misma línea cerrada hasta abajo, para el relleno.
let private area (p: (float * float)[]) =
    let fondo = alto - abajo
    $"{curva p} L{n (fst p[p.Length - 1])},{n fondo} L{n (fst p[0])},{n fondo} Z"

/// Tope de la escala de %: el siguiente múltiplo de 20 % (como mínimo 20 %).
let private topePorcentaje (maximo: float) =
    max 0.2 (Math.Ceiling(Math.Round(maximo * 5.0, 6)) / 5.0)

/// etiquetas = (texto corto del eje, texto largo de la ficha) por periodo.
let lineas (etiquetas: (string * string)[]) (principal: Serie) (secundaria: Serie) (escala: Escala) : GraficaCrono =
    let cantidad = etiquetas.Length

    if cantidad = 0 then
        GraficaCrono.Vacia
    else
        let maximo (s: Serie) = Array.max s.Valores

        let topePrincipal, topeSecundaria =
            match escala with
            | EscalaPropia -> max 1.0 (maximo principal), max 1.0 (maximo secundaria)
            | EscalaPorcentaje ->
                let tope = topePorcentaje (max (maximo principal) (maximo secundaria))
                tope, tope

        // x en % del ancho (con margen a los lados), y en px
        let xPct i = if cantidad = 1 then 50.0 else 6.0 + 88.0 * float i / float (cantidad - 1)
        let y valor tope = arriba + (alto - arriba - abajo) * (1.0 - valor / tope)

        let enPrincipal = principal.Valores |> Array.mapi (fun i v -> xPct i * 10.0, y v topePrincipal)
        let enSecundaria = secundaria.Valores |> Array.mapi (fun i v -> xPct i * 10.0, y v topeSecundaria)

        // Con muchos puntos (p. ej. 120 días) las cifras se pisarían: se
        // ocultan (quedan en la ficha) y el eje muestra unas 12 fechas.
        let conNumeros = cantidad <= 16
        let paso = max 1 (int (ceil (float cantidad / 12.0)))

        // La última etiqueta siempre se escribe; las intermedias, solo si no la pisan.
        let conEtiqueta i =
            i = cantidad - 1 || (i % paso = 0 && cantidad - 1 - i >= max 1 ((paso * 3 + 3) / 4))

        // La pastilla del punto más alto va encima y la del más bajo, debajo:
        // así no se pisan entre ellas.
        let encima yPunto = max 10.0 (yPunto - pastilla)
        let debajo yPunto = min (alto - abajo + pastilla) (yPunto + pastilla)

        let rejilla =
            match escala with
            | EscalaPropia -> [||]
            | EscalaPorcentaje ->
                [| for k in 0 .. int (Math.Round(topePrincipal / 0.2)) ->
                       let valor = float k * 0.2
                       { LineaRejilla.Y = n (y valor topePrincipal); Texto = Formato.PorcentajeEje valor } |]

        { Alto = n alto
          YEje = n (alto - 12.0)
          NombrePrincipal = principal.Nombre
          NombreSecundaria = secundaria.Nombre
          AreaPrincipal = area enPrincipal
          LineaPrincipal = curva enPrincipal
          AreaSecundaria = area enSecundaria
          LineaSecundaria = curva enSecundaria
          Radio = (if cantidad > 40 then "2.5" else "4")
          EscalaPropia = (escala = EscalaPropia)
          ConNumeros = conNumeros
          Rejilla = rejilla
          Puntos =
            etiquetas
            |> Array.mapi (fun i (corta, larga) ->
                let vP, vS = principal.Valores[i], secundaria.Valores[i]
                let yP, yS = snd enPrincipal[i], snd enSecundaria[i]
                let principalArriba = yP <= yS

                { X = n (xPct i)
                  YPrincipal = n yP
                  YSecundaria = n yS
                  Ficha = $"{larga} · {principal.Nombre} {principal.Escribir vP} · {secundaria.Nombre} {secundaria.Escribir vS}"
                  DatoPrincipal = (if conNumeros then principal.Escribir vP else null)
                  YDatoPrincipal = n (if principalArriba then encima yP else debajo yP)
                  DatoSecundaria = (if conNumeros then secundaria.Escribir vS else null)
                  YDatoSecundaria = n (if principalArriba then debajo yS else encima yS)
                  Eje = (if conEtiqueta i then corta else null) }) }
