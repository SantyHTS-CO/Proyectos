// =====================================================================
// Services/TableroTickets.fs — Arma la pestaña «Tickets» (BO Tiendas)
// ---------------------------------------------------------------------
// Recibe la tabla que trajo BigQuery (ya en caché) y los filtros de la
// URL, y calcula con funciones de List todo lo que pinta la vista:
//   - listas desplegables en cascada (Services/Tablero.fs)
//   - matriz "Tickets por agente/team"
//   - matriz "Estado de tickets" por día, con desglose al pulsar el botón
//   - tira de indicadores comparados con el periodo anterior y gráficas
// Todo sale ya escrito (Infraestructura/Formato.fs): la vista solo pinta.
// No vuelve a consultar BigQuery: filtrar ~4.000 filas en memoria tarda
// milisegundos.
//
// Si cambias nombres de columnas en Consultas/bo_tiendas.bqsql, ajústalos
// en el bloque "Columnas que usa el tablero" de abajo.
// =====================================================================

module BO_Tienda.Services.TableroTickets

open System
open BO_Tienda.Infraestructura
open BO_Tienda.Models
open BO_Tienda.Services.Tablero

// ----- Columnas que usa el tablero (nombres del SELECT) -----
let private colTicket      = "id_ticket"        // cruce con la pestaña Motivo
let private colFecha       = "fecha_creacion"   // MES / DIA y filtro de fechas
let private colTipo        = "tipo_ticket"      // columnas de las matrices
let private colSuscripcion = "tipo_suscripcion" // botones Convergente / Solo móvil
let private colSupervisor  = "nom_Super"
let private colTeam        = "nom_Team"
let private colAgente      = "nom_Agente"
let private colMarca       = "marca"
let private colEstado      = "estado_ticket"
let private colMotivo      = "motivo_apertura"
let private colSubMotivo   = "subtipo_ticket"
let private colHorasResol  = "TiempoDeResolucion"          // horas: creación -> cierre
let private colHorasPrimer = "TiempoHastaLaPrimeraGestion" // horas: creación -> asignación
let private colHorasAbiert = "TiempoAbierto"               // horas (solo tickets abiertos)
// «Asignado» = el primer cambio de estado (casi siempre a EN PROGRESO: alguien
// toma el ticket). De ahí sale TiempoHastaLaPrimeraGestion y, junto a la fecha
// de cierre, la medición «asignación -> cierre».
let private colAsignacion  = "fecha_primer_cambio_estado"
let private colCierre      = "fecha_cierre"
let private colTramoCierre = "TiempoHastaElCierre"         // "< 4H", "4H - 24H"…
let private colPrimeraGest = "SOLUCIONADO_PRIMERA_GESTION" // 1 = resuelto en 1ª gestión
let private colAbierto     = "TICKET_ABIERTOS"             // 1 = sigue abierto

// Orden de las barras de "Tiempos de cierre" (lo que no esté aquí va al final)
let private ordenTramos = [ "< 4H"; "4H - 24H"; "1 - 3 días"; "> 3 días"; "Abierto" ]

// Columnas que NO salen en la "Descarga general" (la llave es solo para el cruce)
let private ocultasEnExcel = [ "Llave" ]

/// Un ticket con solo los campos que usan los tableros. "Fila" recuerda su
/// posición en la tabla original para exportar todas las columnas.
/// (internal: la pestaña Motivo lo usa para saber el agente de cada ticket)
type internal Ticket =
    { Fila: int
      Id: string
      Fecha: DateOnly
      Tipo: string
      Suscripcion: string
      Supervisor: string
      Team: string
      Agente: string
      Marca: string
      Estado: string
      Motivo: string
      SubMotivo: string
      HorasResolucion: float option      // None = sin dato (no cuenta en el promedio)
      HorasPrimeraGestion: float option  // creación -> asignación
      HorasAsignadoACierre: float option // asignación -> cierre (solo tickets cerrados)
      HorasAbierto: float option
      TramoCierre: string
      PrimeraGestion: bool
      Abierto: bool }

// ----------------------------------------------------------------
// Lectura
// ----------------------------------------------------------------

/// Cuenta los tickets por tipo para una fila de la matriz.
let private fila (nombre: string) (tickets: Ticket list) =
    { FilaMatriz.Nombre = (if nombre = "" then "(sin dato)" else nombre)
      Conteo = tickets |> List.countBy _.Tipo |> readOnlyDict
      Hijos = [||] }

// Número de horas o None si viene vacío (None ≠ 0: no cuenta en el promedio)
let private horas (v: obj) =
    match v with
    | null
    | :? DBNull -> None
    | v -> Some(Convert.ToDouble v)

// Horas entre dos fechas; None si falta alguna (ticket sin cerrar) o vienen al revés
let private horasEntre (desde: obj) (hasta: obj) =
    match desde, hasta with
    | (:? DateTime as d), (:? DateTime as h) when h >= d -> Some((h - d).TotalHours)
    | _ -> None

/// Pasa de la tabla genérica a una lista de Ticket. Si falta alguna
/// columna avisa cuál, en vez de fallar con un error raro más adelante.
let internal leer (datos: TablaDatos) : Result<Ticket list, string> =
    let necesarias =
        [ colTicket; colFecha; colTipo; colSuscripcion; colSupervisor; colTeam
          colAgente; colMarca; colEstado; colMotivo; colSubMotivo
          colHorasResol; colHorasPrimer; colHorasAbiert; colTramoCierre
          colPrimeraGest; colAbierto; colAsignacion; colCierre ]

    match necesarias |> List.filter (fun c -> datos.Indice c < 0) with
    | _ :: _ as faltan -> Error $"""A la consulta de tickets le faltan columnas: {String.concat ", " faltan}"""
    | [] ->
        let i = datos.Indice
        let iId, iFecha, iTipo, iSus = i colTicket, i colFecha, i colTipo, i colSuscripcion
        let iSup, iTeam, iAg, iMarca = i colSupervisor, i colTeam, i colAgente, i colMarca
        let iEst, iMot, iSub, iRes = i colEstado, i colMotivo, i colSubMotivo, i colHorasResol
        let iPri, iAbi, iTramo = i colHorasPrimer, i colHorasAbiert, i colTramoCierre
        let iPg, iAb = i colPrimeraGest, i colAbierto
        let iAsig, iCierre = i colAsignacion, i colCierre
        let texto = Convertir.texto

        [ for n in 0 .. datos.Filas.Count - 1 do
              let r = datos.Filas[n]
              match r[iFecha] with
              | :? DateTime as fecha ->
                  { Fila = n
                    Id = texto r[iId]
                    Fecha = DateOnly.FromDateTime fecha
                    Tipo = texto r[iTipo]
                    Suscripcion = texto r[iSus]
                    Supervisor = texto r[iSup]
                    Team = texto r[iTeam]
                    Agente = texto r[iAg]
                    Marca = texto r[iMarca]
                    Estado = texto r[iEst]
                    Motivo = texto r[iMot]
                    SubMotivo = texto r[iSub]
                    HorasResolucion = horas r[iRes]
                    HorasPrimeraGestion = horas r[iPri]
                    HorasAsignadoACierre = horasEntre r[iAsig] r[iCierre]
                    HorasAbierto = horas r[iAbi]
                    TramoCierre = texto r[iTramo]
                    PrimeraGestion = Convertir.entero r[iPg] = 1
                    Abierto = Convertir.entero r[iAb] = 1 }
              | _ -> () ]   // sin fecha no se puede ubicar
        |> Ok

// ----------------------------------------------------------------
// Indicadores
// ----------------------------------------------------------------

/// Las cifras de un periodo (actual o anterior).
type private Medidas =
    { Tickets: int
      Primera: int
      Abiertos: int
      HorasResol: float option
      HorasPrimera: float option
      HorasACierre: float option
      HorasAbierto: float option }

    /// First Intervention Rate
    member m.Fir = if m.Tickets = 0 then None else Some(float m.Primera / float m.Tickets)

    /// Solucionados fuera de la 1ª gestión
    member m.Fuera = m.Tickets - m.Primera

let private medir (ts: Ticket list) =
    { Medidas.Tickets = ts.Length
      Primera = ts |> contar _.PrimeraGestion
      Abiertos = ts |> contar _.Abierto
      HorasResol = ts |> List.map _.HorasResolucion |> promedio
      HorasPrimera = ts |> List.map _.HorasPrimeraGestion |> promedio
      HorasACierre = ts |> List.map _.HorasAsignadoACierre |> promedio
      HorasAbierto = ts |> List.map _.HorasAbierto |> promedio }   // TiempoAbierto solo existe en los abiertos

// ----------------------------------------------------------------
// Tablero
// ----------------------------------------------------------------

/// Calcula todo el tablero a partir de los tickets ya leídos (hay al menos uno).
let private armar (vm: TicketsViewModel) (f: FiltrosTickets) (todos: Ticket list) =
    // Rango de fechas: por defecto, todo lo que trae la consulta.
    // Los filtros se completan aquí para que la vista muestre lo que se usó.
    let fechaMin = todos |> List.map _.Fecha |> List.min
    let fechaMax = todos |> List.map _.Fecha |> List.max
    if not f.Desde.HasValue then f.Desde <- Nullable fechaMin
    if not f.Hasta.HasValue then f.Hasta <- Nullable fechaMax
    if f.Vista <> "team" then f.Vista <- "agente"
    f.PeriodoGrafica <- periodoValido "mes" f.PeriodoGrafica
    f.PeriodoTabla <- periodoValido "dia" f.PeriodoTabla
    let desdePanel, hastaPanel = f.Desde.Value, f.Hasta.Value

    // El punto que se pulsó en la gráfica por tiempo recorta el rango del panel
    // (si ya no vale, se olvida)
    let rangoPeriodo = rangoDelPeriodo f.Periodo f.PeriodoGrafica desdePanel hastaPanel
    if rangoPeriodo.IsNone then f.Periodo <- Nullable()
    let desde, hasta = rangoPeriodo |> Option.defaultValue (desdePanel, hastaPanel)

    // 1) Filtros fijos: fechas, tipo de suscripción y lo elegido en las barras
    let cumpleSuscripcion (t: Ticket) = String.IsNullOrEmpty f.Suscripcion || t.Suscripcion = f.Suscripcion

    // «excepto» deja fuera la selección de la gráfica que se está pintando
    // ("Tramo", "TipoTicket"): así cada gráfica sigue mostrando todas sus
    // opciones y se puede cambiar de una a otra con un clic.
    let cumpleBarras excepto (t: Ticket) =
        (excepto = "Tramo" || String.IsNullOrEmpty f.Tramo || t.TramoCierre = f.Tramo)
        && (excepto = "TipoTicket" || String.IsNullOrEmpty f.TipoTicket || t.Tipo = f.TipoTicket)

    let candidatos excepto =
        let a, b = if excepto = "Periodo" then desdePanel, hastaPanel else desde, hasta
        todos |> List.filter (fun t -> enRango a b t.Fecha && cumpleSuscripcion t && cumpleBarras excepto t)

    // Como en el Power BI, solo cuentan los tickets que cruzaron con la
    // nómina (tienen agente). Los demás se informan aparte.
    let baseTickets, sinAgente = candidatos "" |> List.partition (fun t -> t.Agente <> "")

    // 2) Listas desplegables. Lo de siempre a la vista; el resto, en «Más filtros».
    let dims =
        [ dimension "Supervisor" "Supervisor"  (fun (t: Ticket) -> t.Supervisor) f.Supervisor
          dimension "Team"       "Team leader" (fun t -> t.Team)      f.Team
          dimension "Agente"     "Agente"      (fun t -> t.Agente)    f.Agente
          dimension "Marca"      "Marca"       (fun t -> t.Marca)     f.Marca
          dimension "Estado"     "Estado"      (fun t -> t.Estado)    f.Estado    |> masFiltros
          dimension "Motivo"     "Motivo"      (fun t -> t.Motivo)    f.Motivo    |> masFiltros
          dimension "SubMotivo"  "SubMotivo"   (fun t -> t.SubMotivo) f.SubMotivo |> masFiltros ]

    let listas = panel dims baseTickets
    let filtrados = baseTickets |> List.filter (cumple dims)

    // Lo que ve cada gráfica que filtra: todo menos su propia selección
    // ("Periodo", "Tramo", "TipoTicket" o "Estado", que además es una lista del panel)
    let sinSeleccion excepto =
        candidatos excepto
        |> List.filter (fun t -> t.Agente <> "" && dims |> List.forall (fun d -> d.Clave = excepto || d.Cumple t))

    let paraCronologico = sinSeleccion "Periodo"
    let paraEstado = sinSeleccion "Estado"
    let paraTramo = sinSeleccion "Tramo"
    let paraTipo = sinSeleccion "TipoTicket"

    // 3) Matrices
    let grupo: Ticket -> string = if f.Vista = "team" then _.Team else _.Agente

    let porGrupo (ts: Ticket list) =
        ts
        |> List.groupBy grupo
        |> List.sortWith (fun (a, _) (b, _) -> compararTexto a b)
        |> List.map (fun (nombre, g) -> fila nombre g)
        |> Array.ofList

    // Estado de tickets: una fila por día, semana o mes (lo más reciente arriba).
    // Los años se miran en la gráfica por tiempo (que siempre muestra todo el
    // rango) para que las etiquetas no cambien al elegir un punto.
    let variosAnios = (paraCronologico |> List.map _.Fecha.Year |> List.distinct |> List.length) > 1

    let porPeriodo =
        filtrados
        |> List.groupBy (fun t -> inicioPeriodo t.Fecha f.PeriodoTabla)
        |> List.sortByDescending fst
        |> List.map (fun (inicio, g) ->
            { (fila (etiquetaLarga inicio f.PeriodoTabla variosAnios) g) with Hijos = porGrupo g })
        |> Array.ofList

    // 4) Indicadores vs periodo anterior: mismos filtros, mismo número de días
    let antDesde, antHasta, dias = periodoAnterior desde hasta

    let anteriores =
        todos
        |> List.filter (fun t ->
            enRango antDesde antHasta t.Fecha && cumpleSuscripcion t && cumpleBarras "" t && t.Agente <> "" && cumple dims t)

    let act = medir filtrados
    let ant = if List.isEmpty anteriores then None else Some(medir anteriores)
    let delAnterior campo = ant |> Option.bind campo

    // Tickets abiertos es una foto de HOY (los que siguen abiertos): no se
    // compara con el periodo anterior.
    // Siete tarjetas en dos filas (4 + 3, ver .resumen.de-7 en site.css): arriba
    // la calidad y lo abierto; abajo los tiempos, donde resolución = creación ->
    // asignación + asignación -> cierre.
    let indicadores =
        [| tarjeta "Resueltos en 1ª gestión (FIR)" Iconos.Diana
               $"{Formato.Entero act.Primera} en 1ª gestión de {Formato.Entero act.Tickets} tickets"
               (comparar act.Fir (delAnterior _.Fir) CifraPorcentaje Mas true)
           |> destacada
           tarjeta "Solucionados fuera de la 1ª gestión" Iconos.Ticket null
               (comparar (Some(float act.Fuera)) (delAnterior (fun a -> Some(float a.Fuera))) CifraEntera Neutro false)
           tarjeta "Tickets abiertos" Iconos.Bandeja null
               (comparar (Some(float act.Abiertos)) None CifraEntera Menos false)
           tarjeta "Tiempo de ticket abierto" Iconos.Reloj null
               (comparar act.HorasAbierto (delAnterior _.HorasAbierto) CifraDuracion Menos false)
           tarjeta "Tiempo de resolución" Iconos.Check "Desde que se creó hasta el cierre"
               (comparar act.HorasResol (delAnterior _.HorasResol) CifraDuracion Menos false)
           tarjeta "Tiempo hasta la asignación" Iconos.Persona "Desde que se creó hasta que se asignó"
               (comparar act.HorasPrimera (delAnterior _.HorasPrimera) CifraDuracion Menos false)
           tarjeta "Tiempo de asignación a cierre" Iconos.Bandera "Desde que se asignó hasta el cierre"
               (comparar act.HorasACierre (delAnterior _.HorasACierre) CifraDuracion Menos false) |]

    // 5) Gráficas. Todas filtran al pulsarlas: cada una muestra lo filtrado
    // por lo demás (sinSeleccion) y resalta lo que se eligió en ella.
    let cronologico =
        paraCronologico
        |> List.groupBy (fun t -> inicioPeriodo t.Fecha f.PeriodoGrafica)
        |> List.sortBy fst
        |> Array.ofList

    let graficaTiempo =
        Graficas.lineas
            "Periodo"
            (if rangoPeriodo.IsSome then fechaIso f.Periodo.Value else null)
            (cronologico
             |> Array.map (fun (inicio, _) ->
                 etiquetaCorta inicio f.PeriodoGrafica variosAnios,
                 etiquetaLarga inicio f.PeriodoGrafica variosAnios,
                 fechaIso inicio))
            (Graficas.serie "Tickets" (cronologico |> Array.map (fun (_, g) -> float g.Length)) Formato.Numero)
            (Graficas.serie "Primera gestión" (cronologico |> Array.map (fun (_, g) -> float (g |> contar _.PrimeraGestion))) Formato.Numero)
            Graficas.EscalaPropia

    // El mayor de una lista de cantidades (0 si no hay ninguna, p. ej. si lo elegido no deja nada)
    let mayorDe (cantidades: float list) = List.fold max 0.0 cantidades

    let porEstado =
        let cuentas = paraEstado |> List.countBy _.Estado |> List.sortByDescending snd
        let mayor = cuentas |> List.map (snd >> float) |> mayorDe

        cuentas
        |> List.map (fun (estado, n) ->
            { Barra.Nombre = (if estado = "" then "(sin dato)" else estado)
              Valor = n
              Fraccion = fraccion (float n) mayor
              Clave = (if estado = "" then null else estado)   // sin estado no hay filtro posible
              Elegida = estado <> "" && estado = f.Estado })

    let posicionTramo tramo =
        List.tryFindIndex ((=) tramo) ordenTramos |> Option.defaultValue Int32.MaxValue

    let porTramoCierre =
        let tramos =
            paraTramo
            |> List.filter (fun t -> t.TramoCierre <> "")
            |> List.groupBy _.TramoCierre
            |> List.sortBy (fst >> posicionTramo)
        // Las dos barras de cada tramo, a la escala del tramo con más tickets
        let mayor = tramos |> List.map (snd >> List.length >> float) |> mayorDe

        tramos
        |> List.map (fun (tramo, g) ->
            let primera = g |> contar _.PrimeraGestion

            { BarraDoble.Nombre = tramo
              Tickets = g.Length
              PrimeraGestion = primera
              FraccionTickets = fraccion (float g.Length) mayor
              FraccionPrimera = fraccion (float primera) mayor
              Clave = tramo
              Elegida = tramo = f.Tramo })

    let primeraGestionPorTipo =
        let tipos =
            paraTipo
            |> List.groupBy _.Tipo
            |> List.map (fun (tipo, g) -> tipo, float (g |> contar _.PrimeraGestion) / float g.Length)
            |> List.sortByDescending snd
        let mayor = tipos |> List.map snd |> mayorDe

        tipos
        |> List.map (fun (tipo, pct) ->
            { BarraPct.Nombre = tipo
              Porcentaje = pct
              Fraccion = fraccion pct mayor
              Clave = (if tipo = "" then null else tipo)
              Elegida = tipo <> "" && tipo = f.TipoTicket })

    // Lo elegido al pulsar las gráficas, como etiquetas que se quitan con un clic
    let selecciones =
        [| if rangoPeriodo.IsSome then
               { Seleccion.Clave = "Periodo"
                 Texto = $"Periodo: {etiquetaLarga f.Periodo.Value f.PeriodoGrafica variosAnios}" }
           if not (String.IsNullOrEmpty f.Estado) then { Seleccion.Clave = "Estado"; Texto = $"Estado: {f.Estado}" }
           if not (String.IsNullOrEmpty f.Tramo) then { Seleccion.Clave = "Tramo"; Texto = $"Tiempo de cierre: {f.Tramo}" }
           if not (String.IsNullOrEmpty f.TipoTicket) then { Seleccion.Clave = "TipoTicket"; Texto = $"Tipo: {f.TipoTicket}" } |]

    { vm with
        FechaMin = fechaMin
        FechaMax = fechaMax
        Listas = listas.Visibles
        MasListas = listas.Mas
        MasFiltrosActivos = listas.MasActivos
        Selecciones = selecciones
        SinAgente = sinAgente.Length
        FilasDetalle = filtrados |> List.map _.Fila |> Array.ofList

        Columnas = filtrados |> List.map _.Tipo |> List.distinct |> List.sortWith compararTexto |> Array.ofList
        PorGrupo = porGrupo filtrados
        PorPeriodo = porPeriodo
        Total = fila "Total" filtrados

        PeriodoAnterior = textoPeriodo antDesde antHasta dias
        HayPeriodoAnterior = Option.isSome ant
        Indicadores = indicadores

        Cronologico = graficaTiempo
        PorEstado = Array.ofList porEstado
        PorTramoCierre = Array.ofList porTramoCierre
        PrimeraGestionPorTipo = Array.ofList primeraGestionPorTipo }

/// Arma el tablero con lo que haya: si la carga o la lectura fallan, el
/// tablero lleva el error (la vista lo pinta en rojo) en vez de tumbar la página.
let construir (cfg: InformeConfig) (cargado: Result<Cargado<TablaDatos>, string>) (f: FiltrosTickets) =
    let vm = TicketsViewModel.Vacio(cfg, f)

    match cargado with
    | Error e -> { vm with Error = e }
    | Ok c ->
        let vm = { vm with CargadoEn = c.CargadoEn; DuracionCarga = c.Duracion }

        match (try leer c.Datos with ex -> Error ex.Message) with
        | Error e -> { vm with Error = e }
        | Ok [] -> vm
        | Ok todos -> armar vm f todos

/// "Descarga general": Excel con las columnas de la consulta de los tickets filtrados.
let exportarExcel (datos: TablaDatos) (filas: int[]) =
    Excel.exportar datos filas ocultasEnExcel "Tickets"
