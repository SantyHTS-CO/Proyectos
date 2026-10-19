// =====================================================================
// Models/Tickets.fs — Modelos del tablero de tickets (BO Tiendas)
// ---------------------------------------------------------------------
// Replica el Power BI: filtros a la izquierda, matriz por agente/team y
// matriz por día con desplegable. Los filtros viajan en la URL (GET), así
// que un enlace con filtros se puede compartir o guardar en favoritos.
//
// Las vistas Razor (.cshtml) se escriben en C#, así que estos tipos usan
// cosas que C# entiende directo: arrays y "null" para "sin valor". Todo
// llega ya calculado y escrito: la vista solo pone el marcado.
// =====================================================================

namespace BO_Tienda.Models

open System
open System.Collections.Generic
open Microsoft.AspNetCore.Html

/// Filtros elegidos por el usuario. ASP.NET llena esta clase sola a partir
/// de la URL: /Informes/Ver/bo-tiendas?Agente=...&Suscripcion=CONVERGENTE
/// (cada propiedad = un parámetro con el mismo nombre). Por eso es una
/// clase con "get, set": ASP.NET la crea vacía y asigna lo que venga.
type FiltrosTickets() =
    member val Desde = Nullable<DateOnly>() with get, set
    member val Hasta = Nullable<DateOnly>() with get, set
    member val Supervisor: string = null with get, set
    member val Team: string = null with get, set
    member val Agente: string = null with get, set
    member val Marca: string = null with get, set
    member val Estado: string = null with get, set
    member val Motivo: string = null with get, set
    member val SubMotivo: string = null with get, set
    member val Suscripcion: string = null with get, set   // CONVERGENTE | SOLO MOVIL | vacío = ambas
    member val Vista = "agente" with get, set             // agente | team  (botones Team / Agente)
    member val PeriodoGrafica = "mes" with get, set       // mes | semana | dia  (gráfica por tiempo)
    member val PeriodoTabla = "dia" with get, set         // dia | semana | mes  (tabla Estado de tickets)
    // Lo que se elige al pulsar las gráficas (Estado, arriba, también es una lista del panel)
    member val Periodo = Nullable<DateOnly>() with get, set   // primer día del punto elegido en la gráfica por tiempo
    member val Tramo: string = null with get, set             // barra elegida en «Tiempos de cierre»
    member val TipoTicket: string = null with get, set        // barra elegida en «Cierre en primera gestión por tipo» (se llama igual en Motivo)

/// Una lista desplegable del panel: su nombre en la URL, el texto que se
/// muestra, las opciones disponibles y la que está elegida (null = Todas).
type FiltroLista =
    { Clave: string
      Etiqueta: string
      Opciones: string[]
      Elegido: string }

/// Algo elegido al pulsar una gráfica, para mostrarlo como etiqueta que se
/// quita con un clic: el parámetro de la URL y el texto («Estado: ANULADO»).
type Seleccion =
    { Clave: string
      Texto: string }

/// Una fila de las matrices: nombre (agente, team o día) + cuántos tickets
/// hay de cada tipo. Hijos = el desglose al pulsar el botón (solo por día).
type FilaMatriz =
    { Nombre: string
      Conteo: IReadOnlyDictionary<string, int>
      Hijos: FilaMatriz[] }

    member this.Total = Seq.sum this.Conteo.Values

    /// Tickets de un tipo en esta fila (0 si no hay).
    member this.Cantidad(columna: string) =
        match this.Conteo.TryGetValue columna with
        | true, n -> n
        | _ -> 0

/// Las barras son botones: al pulsar una se filtra el informe por su Clave
/// (el valor que filtra; null = no se puede filtrar) y, si ya es la elegida,
/// se quita el filtro.

/// Una barra horizontal: etiqueta + cantidad. Fraccion = largo de la barra
/// frente a la mayor (0–1), ya escrito para CSS ("0.53").
type Barra =
    { Nombre: string
      Valor: int
      Fraccion: string
      Clave: string
      Elegida: bool }

/// Dos barras por etiqueta (tickets vs resueltos en 1ª gestión), las dos
/// a la escala de la mayor cantidad de tickets.
type BarraDoble =
    { Nombre: string
      Tickets: int
      PrimeraGestion: int
      FraccionTickets: string
      FraccionPrimera: string
      Clave: string
      Elegida: bool }

/// Una barra en porcentaje (0..1).
type BarraPct =
    { Nombre: string
      Porcentaje: float
      Fraccion: string
      Clave: string
      Elegida: bool }

/// Una línea horizontal de referencia de la gráfica (solo con escala de %).
type LineaRejilla =
    { Y: string             // altura en px
      Texto: string }       // "40 %"

/// Un punto ya ubicado en la gráfica por tiempo (las dos series).
type PuntoCrono =
    { X: string             // posición horizontal en % del ancho ("12.5")
      YPrincipal: string    // altura en px de cada serie
      YSecundaria: string
      Ficha: string         // «Etiqueta · Serie valor · …»: la ficha al pasar el ratón
      DatoPrincipal: string // pastilla con la cifra; null = no se escribe (demasiados puntos)
      YDatoPrincipal: string
      DatoSecundaria: string
      YDatoSecundaria: string
      Eje: string           // etiqueta del eje; null = no se escribe (pisaría a otra)
      Valor: string         // primer día del periodo ("2026-09-01"): lo que filtra al pulsar el punto
      Elegido: bool }       // es el periodo que se eligió

/// La gráfica por tiempo lista para pintar (Views/Shared/_GraficaLineas).
/// La calcula Infraestructura/Graficas.fs.
type GraficaCrono =
    { Alto: string
      YEje: string
      NombrePrincipal: string
      NombreSecundaria: string
      AreaPrincipal: string
      LineaPrincipal: string
      AreaSecundaria: string
      LineaSecundaria: string
      Radio: string
      EscalaPropia: bool    // true = cada línea a su máximo; false = las dos en la misma escala de %
      ConNumeros: bool      // false = demasiados puntos: las cifras quedan en la ficha
      Elegir: string        // parámetro de la URL que fija el clic en un punto; null = la gráfica no filtra
      Rejilla: LineaRejilla[]
      Puntos: PuntoCrono[] }

    static member Vacia =
        { Alto = "0"; YEje = "0"; NombrePrincipal = ""; NombreSecundaria = ""
          AreaPrincipal = ""; LineaPrincipal = ""; AreaSecundaria = ""; LineaSecundaria = ""
          Radio = "4"; EscalaPropia = true; ConNumeros = false; Elegir = null; Rejilla = [||]; Puntos = [||] }

/// Lo que necesitan las pestañas de un informe (Views/Informes/_Pestanas):
/// qué pestaña está activa y el estado de sus datos.
type PaginaInforme =
    { Config: InformeConfig
      Activa: string        // tickets | motivo
      CargadoEn: DateTime
      DuracionCarga: TimeSpan
      Error: string }

/// Un indicador: valor del periodo elegido, valor del periodo anterior y el
/// cambio ya escrito ("▲ 12,30 %"). Tono = bueno | malo | neutro (verde /
/// rojo / gris) según si el cambio es una mejora.
type Indicador =
    { Valor: string
      Crudo: string         // la cifra sin formato ("41.58"), para que cuente al aparecer; null = sin valor
      TipoCifra: string     // porcentaje | entero | duracion: cómo la escribe site.js mientras cuenta
      Anterior: string      // null = no hay periodo anterior
      Cambio: string        // null = "sin comparación"
      Tono: string }

/// Una tarjeta de la tira de indicadores.
type TarjetaIndicador =
    { Nombre: string
      Icono: IHtmlContent
      Destacada: bool
      Detalle: string       // texto bajo la cifra (null = sin detalle)
      Indicador: Indicador }

/// Todo lo que necesita Views/Informes/Tickets.cshtml.
type TicketsViewModel =
    { Config: InformeConfig
      Filtros: FiltrosTickets
      CargadoEn: DateTime
      DuracionCarga: TimeSpan
      Error: string                       // null = sin error

      FechaMin: DateOnly                  // límites del calendario
      FechaMax: DateOnly
      Listas: FiltroLista[]               // siempre a la vista
      MasListas: FiltroLista[]            // dentro de «Más filtros»
      MasFiltrosActivos: int              // el contador de «Más filtros»
      Selecciones: Seleccion[]            // lo elegido al pulsar las gráficas (etiquetas que se quitan)

      Columnas: string[]                  // tipos de ticket (encabezados)
      PorGrupo: FilaMatriz[]
      PorPeriodo: FilaMatriz[]            // Estado de tickets por día / semana / mes
      Total: FilaMatriz
      SinAgente: int                      // tickets sin cruce con nómina

      // ----- Indicadores (comparados con el periodo anterior) -----
      PeriodoAnterior: string             // "01/09/2026 – 15/09/2026 (15 días)"
      HayPeriodoAnterior: bool
      Indicadores: TarjetaIndicador[]

      // ----- Gráficas -----
      Cronologico: GraficaCrono           // tickets y 1ª gestión por mes / semana / día
      PorEstado: Barra[]
      PorTramoCierre: BarraDoble[]        // tickets y 1ª gestión por tramo
      PrimeraGestionPorTipo: BarraPct[]

      /// Posiciones (en TablaDatos.Filas) de los tickets filtrados: lo que
      /// exporta "Descarga general". No se pinta en la vista.
      FilasDetalle: int[] }

    /// Para las pestañas de arriba
    member this.Pagina =
        { PaginaInforme.Config = this.Config
          Activa = "tickets"
          CargadoEn = this.CargadoEn
          DuracionCarga = this.DuracionCarga
          Error = this.Error }

    /// Tablero sin datos (antes de cargar, o si la carga falló).
    static member Vacio(config, filtros) =
        { Config = config
          Filtros = filtros
          CargadoEn = DateTime.MinValue
          DuracionCarga = TimeSpan.Zero
          Error = null
          FechaMin = DateOnly.MinValue
          FechaMax = DateOnly.MinValue
          Listas = [||]
          MasListas = [||]
          MasFiltrosActivos = 0
          Selecciones = [||]
          Columnas = [||]
          PorGrupo = [||]
          PorPeriodo = [||]
          Total = { FilaMatriz.Nombre = "Total"; Conteo = readOnlyDict []; Hijos = [||] }
          SinAgente = 0
          PeriodoAnterior = ""
          HayPeriodoAnterior = false
          Indicadores = [||]
          Cronologico = GraficaCrono.Vacia
          PorEstado = [||]
          PorTramoCierre = [||]
          PrimeraGestionPorTipo = [||]
          FilasDetalle = [||] }
