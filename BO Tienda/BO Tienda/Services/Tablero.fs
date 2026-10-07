// =====================================================================
// Services/Tablero.fs — Piezas comunes de los tableros (Tickets y Motivo)
// ---------------------------------------------------------------------
//   - periodos (día / semana / mes) y sus etiquetas
//   - listas desplegables en cascada, con «Más filtros» (guía de estilos 4)
//   - indicadores comparados con el periodo anterior
// =====================================================================

module internal BO_Tienda.Services.Tablero

open System
open System.Globalization
open Microsoft.AspNetCore.Html
open BO_Tienda.Infraestructura
open BO_Tienda.Models

let Es = Formato.Es

/// Orden alfabético en español (la Á va junto a la A, no después de la Z).
let compararTexto (a: string) (b: string) = Es.CompareInfo.Compare(a, b)

/// Cuántos elementos de la lista cumplen la condición.
let contar condicion lista = lista |> List.filter condicion |> List.length

/// Cuántos valores distintos hay de un campo.
let distintos campo lista = lista |> List.map campo |> List.distinct |> List.length

let enRango (desde: DateOnly) (hasta: DateOnly) (fecha: DateOnly) = fecha >= desde && fecha <= hasta

// ----------------------------------------------------------------
// Periodos: día / semana / mes
// ----------------------------------------------------------------

/// Un periodo raro en la URL -> el de por defecto.
let periodoValido porDefecto periodo =
    match periodo with
    | "mes" | "semana" | "dia" -> periodo
    | _ -> porDefecto

/// Primer día del periodo al que pertenece la fecha. Las semanas van
/// de lunes a domingo (semana ISO, la que se usa en España/Colombia).
let inicioPeriodo (fecha: DateOnly) periodo =
    match periodo with
    | "mes" -> DateOnly(fecha.Year, fecha.Month, 1)
    | "semana" -> fecha.AddDays(-((int fecha.DayOfWeek + 6) % 7))   // retrocede hasta el lunes
    | _ -> fecha

let private numeroSemana (lunes: DateOnly) = ISOWeek.GetWeekOfYear(lunes.ToDateTime TimeOnly.MinValue)

let private ddMM (fecha: DateOnly) = fecha.ToString("dd/MM", CultureInfo.InvariantCulture)

/// Texto corto para el eje de la gráfica: "septiembre", "S38", "09/22".
let etiquetaCorta (inicio: DateOnly) periodo variosAnios =
    match periodo with
    | "mes" -> inicio.ToString((if variosAnios then "MMM yy" else "MMMM"), Es)
    | "semana" -> $"S{numeroSemana inicio}"
    | _ -> inicio.ToString("MM/dd", CultureInfo.InvariantCulture)

/// Texto largo para la tabla y la ficha: "Sem 38 · 15/09–21/09".
let etiquetaLarga (inicio: DateOnly) periodo variosAnios =
    match periodo with
    | "mes" -> inicio.ToString((if variosAnios then "MMMM yyyy" else "MMMM"), Es)
    | "semana" -> $"Sem {numeroSemana inicio} · {ddMM inicio}–{ddMM (inicio.AddDays 6)}"
    | _ -> inicio.ToString("MM/dd", CultureInfo.InvariantCulture)

/// Mismo número de días, justo antes de «Desde» (15 días -> los 15 días previos).
let periodoAnterior (desde: DateOnly) (hasta: DateOnly) =
    let dias = hasta.DayNumber - desde.DayNumber + 1
    desde.AddDays(-dias), desde.AddDays(-1), dias

let textoPeriodo (desde: DateOnly) (hasta: DateOnly) (dias: int) =
    $"""{desde.ToString "dd/MM/yyyy"} – {hasta.ToString "dd/MM/yyyy"} ({dias} días)"""

// ----------------------------------------------------------------
// Listas desplegables
// ----------------------------------------------------------------

/// Un filtro de lista: de qué campo saca el valor, qué eligió el usuario y
/// si va siempre a la vista o dentro de «Más filtros».
type Dimension<'T> =
    { Clave: string
      Etiqueta: string
      Valor: 'T -> string
      Elegido: string
      MasFiltros: bool }

    member d.Cumple(t: 'T) = String.IsNullOrEmpty d.Elegido || d.Valor t = d.Elegido
    member d.Activa = not (String.IsNullOrEmpty d.Elegido)

let dimension clave etiqueta (valor: 'T -> string) elegido =
    { Dimension.Clave = clave; Etiqueta = etiqueta; Valor = valor; Elegido = elegido; MasFiltros = false }

let masFiltros (d: Dimension<'T>) = { d with MasFiltros = true }

/// Pasa todos los filtros de lista.
let cumple (dims: Dimension<'T> list) (t: 'T) = dims |> List.forall (fun d -> d.Cumple t)

type Panel =
    { Visibles: FiltroLista[]
      Mas: FiltroLista[]
      MasActivos: int }

/// Listas en cascada, como los segmentadores de Power BI: cada lista ofrece
/// solo lo que queda con los DEMÁS filtros (elegir un supervisor acota los
/// teams). Una lista con menos de dos opciones se oculta, salvo que tenga
/// algo elegido.
let panel (dims: Dimension<'T> list) (filas: 'T list) =
    let conOpciones =
        [ for d in dims ->
              let opciones =
                  filas
                  |> List.filter (fun t -> dims |> List.forall (fun o -> o.Clave = d.Clave || o.Cumple t))
                  |> List.map d.Valor
                  |> List.filter (fun v -> v <> "")
                  |> List.distinct
                  |> List.sortWith compararTexto

              // Si lo elegido ya no está (por otro filtro), lo dejamos visible
              // para que el usuario vea por qué no salen datos y pueda quitarlo.
              let opciones =
                  if d.Activa && not (List.contains d.Elegido opciones) then d.Elegido :: opciones else opciones

              d,
              { FiltroLista.Clave = d.Clave
                Etiqueta = d.Etiqueta
                Opciones = Array.ofList opciones
                Elegido = d.Elegido } ]

    let visibles enMasFiltros =
        [| for d, lista in conOpciones do
               if d.MasFiltros = enMasFiltros && (d.Activa || lista.Opciones.Length >= 2) then lista |]

    { Visibles = visibles false
      Mas = visibles true
      MasActivos = dims |> List.filter (fun d -> d.MasFiltros && d.Activa) |> List.length }

// ----------------------------------------------------------------
// Indicadores
// ----------------------------------------------------------------

let promedio (valores: float option list) =
    match List.choose id valores with
    | [] -> None
    | conDato -> Some(List.average conDato)

/// Qué clase de cifra es: cómo se escribe y cómo la hace contar site.js.
type Cifra =
    | CifraPorcentaje   // fracción 0–1 -> "41,58 %"
    | CifraEntera       // volumen -> "1.234"
    | CifraDuracion     // horas -> "31h 06m"
    | CifraDecimal      // promedio -> "1,32"

let escribir cifra v =
    match cifra with
    | CifraPorcentaje -> Formato.Porcentaje v
    | CifraEntera -> Formato.Numero v
    | CifraDuracion -> Formato.Duracion v
    | CifraDecimal -> Formato.Decimal v

/// La cifra sin formato para data-valor (el porcentaje, ya en tanto por cien).
let private crudo cifra v =
    match cifra with
    | CifraPorcentaje -> Formato.Invariante(v * 100.0), "porcentaje"
    | CifraEntera -> Formato.Invariante v, "entero"
    | CifraDuracion -> Formato.Invariante v, "duracion"
    | CifraDecimal -> Formato.Invariante v, "decimal"

/// Qué dirección es "buena": más (FIR), menos (tiempos, mismo motivo) o ninguna (volúmenes).
type Mejor =
    | Mas
    | Menos
    | Neutro

/// Arma el indicador: valor actual + cambio vs anterior (▲/▼ y %, o puntos
/// porcentuales si el indicador ya es un %) + color según si mejora.
let comparar (actual: float option) (anterior: float option) cifra mejor enPuntos =
    let valorCrudo, tipo =
        match actual with
        | Some v -> crudo cifra v
        | None -> null, snd (crudo cifra 0.0)

    let ind =
        { Indicador.Valor = actual |> Option.map (escribir cifra) |> Option.defaultValue "—"
          Crudo = valorCrudo
          TipoCifra = tipo
          Anterior = anterior |> Option.map (escribir cifra) |> Option.toObj
          Cambio = null
          Tono = "neutro" }

    match actual, anterior with
    // Si el periodo anterior fue 0 no hay base para un %: la tarjeta
    // muestra "sin comparación" (los % sí se comparan, van en puntos).
    | Some act, Some ant when enPuntos || ant <> 0.0 ->
        let dif = act - ant
        // Un cambio que se vería como 0,00 al redondear cuenta como "sin cambio"
        let relativo = if enPuntos then dif * 100.0 else dif / ant * 100.0
        let dif = if abs relativo < 0.005 then 0.0 else dif
        let flecha = if dif > 0.0 then "▲" elif dif < 0.0 then "▼" else "="

        let cambio =
            if enPuntos then $"{flecha} {Formato.Puntos(abs (dif * 100.0))}"
            else $"{flecha} {Formato.Porcentaje(abs (dif / ant))}"

        let tono =
            if dif = 0.0 || mejor = Neutro then "neutro"
            elif (dif < 0.0) = (mejor = Menos) then "bueno"
            else "malo"

        { ind with Cambio = cambio; Tono = tono }
    | _ -> ind

let tarjeta nombre (icono: IHtmlContent) detalle indicador =
    { TarjetaIndicador.Nombre = nombre; Icono = icono; Destacada = false; Detalle = detalle; Indicador = indicador }

let destacada (t: TarjetaIndicador) = { t with Destacada = true }

/// Largo de una barra frente a la mayor (0–1), escrito para CSS.
let fraccion (valor: float) (maximo: float) =
    Formato.Invariante(if maximo <= 0.0 then 0.0 else valor / maximo)
