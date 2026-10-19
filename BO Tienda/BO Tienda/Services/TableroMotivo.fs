// =====================================================================
// Services/TableroMotivo.fs — Arma la pestaña «Motivo» (BO Tiendas)
// ---------------------------------------------------------------------
// Llamadas entrantes (DataOrb) de la línea del ticket entre el día del
// ticket y los 3 días siguientes (Consultas/bo_tiendas_motivo.bqsql), y si
// su motivo es el mismo o diferente. Cada llamada se cruza por id_ticket
// con la pestaña Tickets para saber supervisor, team, agente y marca, y
// para contar los tickets del periodo.
//
// Cómo se cuenta (las 5 tarjetas)
//   Tickets                         tickets del periodo (pestaña Tickets, mismos filtros)
//   Tickets que volvieron a llamar  tickets distintos con al menos una llamada
//   Llamadas                        llamadas distintas (externalConversationId) de esos tickets
//   Mismo motivo                    % de esas llamadas cuyo motivo está en el catálogo
//   Diferente motivo                % del resto
// Como en Tickets, solo cuentan los tickets que cruzan con la nómina.
// =====================================================================

module BO_Tienda.Services.TableroMotivo

open System
open System.Collections.Generic
open BO_Tienda.Infraestructura
open BO_Tienda.Models
open BO_Tienda.Services.Tablero

// ----- Columnas que usa la pestaña (nombres del SELECT) -----
let private colTicket       = "id_ticket"
let private colFecha        = "fecha_creacion"          // del ticket: filtro de fechas y gráfica
let private colConversacion = "externalConversationId"
let private colMotivo2      = "Motivo_2"
let private colMotivo3      = "Motivo_3"
let private colTipo         = "Motivo_rellamada"        // Mismo motivo | Diferente motivo

let private mismoMotivo = "Mismo motivo"
let private diferenteMotivo = "Diferente motivo"

/// Grupos del ranking con menos llamadas: al final y en gris (guía de estilos 5).
let private minimoRanking = 20

/// Días tras el ticket en los que se miran sus llamadas.
let private ventanaDias = 3

/// Una fila de la consulta (un ticket y una de sus llamadas), con lo que
/// se sabe de su ticket.
type private Llamada =
    { Fila: int
      Ticket: string
      Fecha: DateOnly
      Conversacion: string
      Motivo2: string
      Motivo3: string
      Mismo: bool
      // De la pestaña Tickets, por id_ticket ("" si el ticket no cruza)
      Supervisor: string
      Team: string
      Agente: string
      Marca: string
      TipoTicket: string }

// ----------------------------------------------------------------
// Lectura
// ----------------------------------------------------------------

let private leer (datos: TablaDatos) (tickets: IReadOnlyDictionary<string, TableroTickets.Ticket>) =
    let necesarias = [ colTicket; colFecha; colConversacion; colMotivo2; colMotivo3; colTipo ]

    match necesarias |> List.filter (fun c -> datos.Indice c < 0) with
    | _ :: _ as faltan -> Error $"""A la consulta de motivo le faltan columnas: {String.concat ", " faltan}"""
    | [] ->
        let i = datos.Indice
        let iTicket, iFecha, iConv = i colTicket, i colFecha, i colConversacion
        let iM2, iM3, iTipo = i colMotivo2, i colMotivo3, i colTipo
        let texto = Convertir.texto

        [ for n in 0 .. datos.Filas.Count - 1 do
              let r = datos.Filas[n]
              match r[iFecha] with
              | :? DateTime as fecha ->
                  let id = texto r[iTicket]

                  let ticket =
                      match tickets.TryGetValue id with
                      | true, t -> Some t
                      | _ -> None

                  let delTicket (campo: TableroTickets.Ticket -> string) =
                      ticket |> Option.map campo |> Option.defaultValue ""

                  { Fila = n
                    Ticket = id
                    Fecha = DateOnly.FromDateTime fecha
                    Conversacion = texto r[iConv]
                    Motivo2 = texto r[iM2]
                    Motivo3 = texto r[iM3]
                    Mismo = texto r[iTipo] = mismoMotivo
                    Supervisor = delTicket _.Supervisor
                    Team = delTicket _.Team
                    Agente = delTicket _.Agente
                    Marca = delTicket _.Marca
                    TipoTicket = delTicket _.Tipo }
              | _ -> () ]
        |> Ok

/// Los tickets por id (si un id sale dos veces, vale el primero).
let private indice (tickets: TableroTickets.Ticket list) =
    let porId = Dictionary<string, TableroTickets.Ticket>()
    for t in tickets do
        porId.TryAdd(t.Id, t) |> ignore
    porId :> IReadOnlyDictionary<_, _>

// ----------------------------------------------------------------
// Medidas
// ----------------------------------------------------------------

type private Medidas =
    { Tickets: int        // tickets del periodo (pestaña Tickets)
      ConLlamada: int     // tickets que volvieron a llamar
      Llamadas: int       // llamadas distintas de esos tickets
      Mismo: int }        // llamadas con el mismo motivo

    member m.Tasa = if m.Tickets = 0 then None else Some(float m.ConLlamada / float m.Tickets)
    member m.PctMismo = if m.Llamadas = 0 then None else Some(float m.Mismo / float m.Llamadas)
    member m.PctDiferente = m.PctMismo |> Option.map (fun p -> 1.0 - p)
    member m.Diferente = m.Llamadas - m.Mismo
    member m.PorTicket = if m.ConLlamada = 0 then None else Some(float m.Llamadas / float m.ConLlamada)

/// Una llamada cuenta una vez aunque cruce con dos tickets de la misma línea.
let private medir (ls: Llamada list) (tickets: int) =
    let llamadas = ls |> List.distinctBy _.Conversacion

    { Medidas.Tickets = tickets
      ConLlamada = ls |> distintos _.Ticket
      Llamadas = llamadas.Length
      Mismo = llamadas |> contar _.Mismo }

// ----------------------------------------------------------------
// Ranking
// ----------------------------------------------------------------

let private columna titulo sentido calor =
    { ColumnaRanking.Titulo = titulo; Orden = "numero"; Sentido = sentido; Calor = calor }

let private entero (n: int) = { CeldaRanking.Texto = Formato.Entero n; Valor = string n; Clase = null }

let private pct (v: float option) =
    match v with
    | Some p -> { CeldaRanking.Texto = Formato.Porcentaje p; Valor = Formato.Invariante(p * 100.0); Clase = null }
    | None -> { CeldaRanking.Texto = "—"; Valor = null; Clase = null }

let private vacia = { CeldaRanking.Texto = ""; Valor = null; Clase = null }

/// El parámetro (y la lista del panel) que filtra la fila de un ranking según «Ver por».
let private filtroDelRanking ver =
    match ver with
    | "motivo3" -> "Motivo3"
    | "agente" -> "Agente"
    | "team" -> "Team"
    | _ -> "Motivo2"

/// Filas del ranking según «Ver por». Las de pocas llamadas van al final.
/// Pulsar una fila filtra por ella («elegido» = la ya elegida, que sale resaltada).
let private ranking (ver: string) (elegido: string) (filtradas: Llamada list) (total: Medidas) (ticketsPor: (string -> int) option) =
    // (clave que filtra, nombre que se ve, texto pequeño, llamadas del grupo)
    let grupos: (string * string * string * Llamada list) list =
        let sinDato v = if v = "" then "(sin dato)" else v

        match ver with
        | "motivo3" ->
            filtradas
            |> List.groupBy (fun l -> l.Motivo2, l.Motivo3)
            |> List.map (fun ((m2, m3), g) -> m3, sinDato m3, sinDato m2, g)
        | "agente" -> filtradas |> List.groupBy _.Agente |> List.map (fun (a, g) -> a, sinDato a, null, g)
        | "team" -> filtradas |> List.groupBy _.Team |> List.map (fun (t, g) -> t, sinDato t, null, g)
        | _ -> filtradas |> List.groupBy _.Motivo2 |> List.map (fun (m, g) -> m, sinDato m, null, g)

    let porPersona = ver = "agente" || ver = "team"

    let columnas, celdas, totales =
        if porPersona then
            [| columna "Tickets" null false
               columna "Volvieron a llamar" null false
               columna "% Volvieron a llamar" "-1" false
               columna "Llamadas" null true
               columna "% Mismo motivo" "-1" false
               columna "% Diferente motivo" "0" false |],
            (fun (nombre: string) (m: Medidas) ->
                let tickets = ticketsPor |> Option.map (fun contarDe -> contarDe nombre) |> Option.defaultValue 0
                let m = { m with Tickets = tickets }
                [| entero m.Tickets; entero m.ConLlamada; pct m.Tasa; entero m.Llamadas; pct m.PctMismo; pct m.PctDiferente |]),
            [| entero total.Tickets; entero total.ConLlamada; pct total.Tasa; entero total.Llamadas
               pct total.PctMismo; pct total.PctDiferente |]
        elif ver = "motivo3" then
            [| columna "Llamadas" null true
               columna "% del total" null false
               columna "Tickets" null false
               { ColumnaRanking.Titulo = "Tipo de motivo"; Orden = "texto"; Sentido = null; Calor = false } |],
            (fun _ (m: Medidas) ->
                let tipo = if m.Mismo > 0 then mismoMotivo else diferenteMotivo
                [| entero m.Llamadas
                   pct (Some(float m.Llamadas / float (max 1 total.Llamadas)))
                   entero m.ConLlamada
                   { CeldaRanking.Texto = tipo; Valor = null; Clase = (if m.Mismo > 0 then "chip-mismo" else "chip-diferente") } |]),
            [| entero total.Llamadas; pct (Some 1.0); entero total.ConLlamada; vacia |]
        else
            [| columna "Llamadas" null true
               columna "% del total" null false
               columna "Tickets" null false
               columna "% Mismo motivo" "-1" false
               columna "% Diferente motivo" "0" false |],
            (fun _ (m: Medidas) ->
                [| entero m.Llamadas
                   pct (Some(float m.Llamadas / float (max 1 total.Llamadas)))
                   entero m.ConLlamada
                   pct m.PctMismo
                   pct m.PctDiferente |]),
            [| entero total.Llamadas; pct (Some 1.0); entero total.ConLlamada; pct total.PctMismo; pct total.PctDiferente |]

    let filas =
        grupos
        |> List.map (fun (clave, nombre, detalle, g) ->
            let m = medir g 0

            m.Llamadas,
            { FilaRanking.Nombre = nombre
              Detalle = detalle
              Celdas = celdas nombre m
              Pocas = m.Llamadas < minimoRanking
              Clave = (if clave = "" then null else clave)   // «(sin dato)» no se puede filtrar
              Elegida = clave <> "" && clave = elegido })
        // De más a menos llamadas, y las de pocas al final
        |> List.sortBy (fun (llamadas, f) -> f.Pocas, -llamadas)
        |> List.map snd

    { Agrupacion =
        (match ver with
         | "motivo3" -> "Motivo 3"
         | "agente" -> "Agente"
         | "team" -> "Team"
         | _ -> "Motivo 2")
      Filtro = filtroDelRanking ver
      Columnas = columnas
      Filas = Array.ofList filas
      Total = totales }

// ----------------------------------------------------------------
// Pestaña
// ----------------------------------------------------------------

let private armar (vm: MotivoViewModel) (f: FiltrosMotivo) (tickets: TableroTickets.Ticket list) (todas: Llamada list) =
    let fechaMin = todas |> List.map _.Fecha |> List.min
    let fechaMax = todas |> List.map _.Fecha |> List.max
    if not f.Desde.HasValue then f.Desde <- Nullable fechaMin
    if not f.Hasta.HasValue then f.Hasta <- Nullable fechaMax
    f.Ver <- (match f.Ver with
              | "motivo3" | "agente" | "team" -> f.Ver
              | _ -> "motivo2")
    f.PeriodoMotivo <- periodoValido "semana" f.PeriodoMotivo
    let desdePanel, hastaPanel = f.Desde.Value, f.Hasta.Value

    // El punto que se pulsó en la gráfica por tiempo recorta el rango del panel
    // (si ya no vale, se olvida)
    let rangoPeriodo = rangoDelPeriodo f.PeriodoLlamadas f.PeriodoMotivo desdePanel hastaPanel
    if rangoPeriodo.IsNone then f.PeriodoLlamadas <- Nullable()
    let desde, hasta = rangoPeriodo |> Option.defaultValue (desdePanel, hastaPanel)

    // Mismo / Diferente motivo (botones del panel)
    let cumpleTipo (l: Llamada) =
        String.IsNullOrEmpty f.TipoMotivo || (if l.Mismo then mismoMotivo else diferenteMotivo) = f.TipoMotivo

    // «excepto» = "Periodo": la gráfica por tiempo ignora su propia selección
    // y siempre muestra todo el rango del panel
    let candidatas excepto =
        let a, b = if excepto = "Periodo" then desdePanel, hastaPanel else desde, hasta
        todas |> List.filter (fun l -> enRango a b l.Fecha && cumpleTipo l)

    // Como en Tickets: solo cuentan los tickets que cruzan con la nómina
    let baseLlamadas, sinAgente = candidatas "" |> List.partition (fun l -> l.Agente <> "")

    let dims =
        [ dimension "Supervisor" "Supervisor"     (fun (l: Llamada) -> l.Supervisor) f.Supervisor
          dimension "Team"       "Team leader"    (fun l -> l.Team)       f.Team
          dimension "Agente"     "Agente"         (fun l -> l.Agente)     f.Agente
          dimension "Marca"      "Marca"          (fun l -> l.Marca)      f.Marca
          dimension "Motivo2"    "Motivo 2"       (fun l -> l.Motivo2)    f.Motivo2
          dimension "Motivo3"    "Motivo 3"       (fun l -> l.Motivo3)    f.Motivo3    |> masFiltros
          dimension "TipoTicket" "Tipo de ticket" (fun l -> l.TipoTicket) f.TipoTicket |> masFiltros ]

    let listas = panel dims baseLlamadas
    let filtradas = baseLlamadas |> List.filter (cumple dims)

    // Lo que ven el ranking (sus filas) y la gráfica por tiempo, que filtran al
    // pulsarlos: todo menos su propia selección, para que sigan mostrando todas
    // sus opciones y se pueda cambiar de una a otra con un clic.
    let sinSeleccion excepto =
        candidatas excepto
        |> List.filter (fun l -> l.Agente <> "" && dims |> List.forall (fun d -> d.Clave = excepto || d.Cumple l))

    let filtroRanking = filtroDelRanking f.Ver
    let paraRanking = sinSeleccion filtroRanking
    let paraCronologico = sinSeleccion "Periodo"

    // Tickets del periodo (tarjeta 1): los de la pestaña Tickets con los
    // filtros que son del ticket (los de motivo son de la llamada).
    // «excepto» = la lista del panel que no se aplica (la del ranking).
    let ticketsEn excepto a b =
        let deTicket (t: TableroTickets.Ticket) =
            [ "Supervisor", f.Supervisor, t.Supervisor
              "Team", f.Team, t.Team
              "Agente", f.Agente, t.Agente
              "Marca", f.Marca, t.Marca
              "TipoTicket", f.TipoTicket, t.Tipo ]
            |> List.forall (fun (clave, elegido, valor) -> clave = excepto || String.IsNullOrEmpty elegido || elegido = valor)

        tickets |> List.filter (fun t -> t.Agente <> "" && enRango a b t.Fecha && deTicket t)

    let ticketsDelPeriodo = ticketsEn "" desde hasta
    let ticketsDelRanking = ticketsEn filtroRanking desde hasta

    // Tarjetas vs periodo anterior: mismos filtros, mismo número de días
    let antDesde, antHasta, dias = periodoAnterior desde hasta

    let anteriores =
        todas
        |> List.filter (fun l -> enRango antDesde antHasta l.Fecha && cumpleTipo l && l.Agente <> "" && cumple dims l)

    let act = medir filtradas ticketsDelPeriodo.Length
    let ant = if List.isEmpty anteriores then None else Some(medir anteriores (ticketsEn "" antDesde antHasta).Length)
    let delAnterior campo = ant |> Option.bind campo
    let volumen (campo: Medidas -> int) = Some(float (campo act)), delAnterior (fun a -> Some(float (campo a)))

    let indicadores =
        let ticketsAct, ticketsAnt = volumen _.Tickets
        let conLlamadaAct, conLlamadaAnt = volumen _.ConLlamada
        let llamadasAct, llamadasAnt = volumen _.Llamadas

        [| tarjeta "Tickets" Iconos.Ticket null
               (comparar ticketsAct ticketsAnt CifraEntera Neutro false)
           tarjeta "Tickets que volvieron a llamar" Iconos.Repetir
               (act.Tasa |> Option.map (fun t -> $"{Formato.Porcentaje t} de los tickets") |> Option.toObj)
               (comparar conLlamadaAct conLlamadaAnt CifraEntera Neutro false)
           tarjeta "Llamadas de esos tickets" Iconos.Telefono
               (act.PorTicket |> Option.map (fun p -> $"{Formato.Decimal p} por ticket") |> Option.toObj)
               (comparar llamadasAct llamadasAnt CifraEntera Neutro false)
           tarjeta "Mismo motivo" Iconos.Diana $"{Formato.Entero act.Mismo} llamadas"
               (comparar act.PctMismo (delAnterior _.PctMismo) CifraPorcentaje Menos true)
           |> destacada
           tarjeta "Diferente motivo" Iconos.Desvio $"{Formato.Entero act.Diferente} llamadas"
               (comparar act.PctDiferente (delAnterior _.PctDiferente) CifraPorcentaje Neutro true) |]

    // El ranking (sin su propia selección) con sus totales y los tickets por agente o team
    let medidasRanking = medir paraRanking ticketsDelRanking.Length

    let ticketsPor =
        let contarPor (campo: TableroTickets.Ticket -> string) =
            let cuenta = ticketsDelRanking |> List.countBy campo |> dict
            Some(fun nombre -> match cuenta.TryGetValue nombre with | true, n -> n | _ -> 0)

        match f.Ver with
        | "agente" -> contarPor _.Agente
        | "team" -> contarPor _.Team
        | _ -> None

    let elegidoRanking =
        match f.Ver with
        | "motivo3" -> f.Motivo3
        | "agente" -> f.Agente
        | "team" -> f.Team
        | _ -> f.Motivo2

    // Gráfica: % mismo / diferente motivo por periodo (fecha del ticket)
    let variosAnios = (paraCronologico |> List.map _.Fecha.Year |> List.distinct |> List.length) > 1

    let porPeriodo =
        paraCronologico
        |> List.groupBy (fun l -> inicioPeriodo l.Fecha f.PeriodoMotivo)
        |> List.sortBy fst
        |> Array.ofList

    let pctMismo = porPeriodo |> Array.map (fun (_, g) -> (medir g 0).PctMismo |> Option.defaultValue 0.0)

    let motivos =
        Graficas.lineas
            "PeriodoLlamadas"
            (if rangoPeriodo.IsSome then fechaIso f.PeriodoLlamadas.Value else null)
            (porPeriodo
             |> Array.map (fun (inicio, _) ->
                 etiquetaCorta inicio f.PeriodoMotivo variosAnios,
                 etiquetaLarga inicio f.PeriodoMotivo variosAnios,
                 fechaIso inicio))
            (Graficas.serie "Mismo motivo" pctMismo Formato.Porcentaje)
            (Graficas.serie "Diferente motivo" (pctMismo |> Array.map (fun p -> 1.0 - p)) Formato.Porcentaje)
            Graficas.EscalaPorcentaje

    // Lo elegido al pulsar la gráfica o el ranking, como etiquetas que se quitan con un clic
    let selecciones =
        [| if rangoPeriodo.IsSome then
               { Seleccion.Clave = "PeriodoLlamadas"
                 Texto = $"Periodo: {etiquetaLarga f.PeriodoLlamadas.Value f.PeriodoMotivo variosAnios}" }
           for clave, etiqueta, valor in
               [ "Motivo2", "Motivo 2", f.Motivo2
                 "Motivo3", "Motivo 3", f.Motivo3
                 "Agente", "Agente", f.Agente
                 "Team", "Team", f.Team ] do
               if not (String.IsNullOrEmpty valor) then { Seleccion.Clave = clave; Texto = $"{etiqueta}: {valor}" } |]

    { vm with
        FechaMin = fechaMin
        FechaMax = fechaMax
        Listas = listas.Visibles
        MasListas = listas.Mas
        MasFiltrosActivos = listas.MasActivos
        Selecciones = selecciones
        HayDatos = not (List.isEmpty filtradas)
        SinAgente = sinAgente |> distintos _.Conversacion
        VentanaAbierta = hasta.AddDays ventanaDias >= DateOnly.FromDateTime DateTime.Today
        PeriodoAnterior = textoPeriodo antDesde antHasta dias
        HayPeriodoAnterior = Option.isSome ant
        Indicadores = indicadores
        Ranking = ranking f.Ver elegidoRanking paraRanking medidasRanking ticketsPor
        Motivos = motivos
        FilasDetalle = filtradas |> List.map _.Fila |> Array.ofList }

/// Arma la pestaña con lo que haya: si una carga o la lectura fallan, la
/// pestaña lleva el error (la vista lo pinta en rojo) en vez de tumbarse.
let construir (cfg: InformeConfig) (tickets: Result<Cargado<TablaDatos>, string>)
              (llamadas: Result<Cargado<TablaDatos>, string>) (f: FiltrosMotivo) =
    let vm = MotivoViewModel.Vacio(cfg, f)

    match tickets, llamadas with
    | Error e, _ -> { vm with Error = $"Tickets: {e}" }
    | _, Error e -> { vm with Error = $"Motivo: {e}" }
    | Ok t, Ok l ->
        let vm = { vm with CargadoEn = l.CargadoEn; DuracionCarga = l.Duracion }

        let leido =
            try
                match TableroTickets.leer t.Datos with
                | Error e -> Error e
                | Ok ts -> leer l.Datos (indice ts) |> Result.map (fun ls -> ts, ls)
            with ex ->
                Error ex.Message

        match leido with
        | Error e -> { vm with Error = e }
        | Ok(_, []) -> vm
        | Ok(ts, ls) -> armar vm f ts ls

/// "Descarga general": Excel con las columnas de la consulta de las llamadas filtradas.
let exportarExcel (datos: TablaDatos) (filas: int[]) =
    Excel.exportar datos filas [] "Motivo"
