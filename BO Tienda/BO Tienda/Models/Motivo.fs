// =====================================================================
// Models/Motivo.fs — Modelos de la pestaña «Motivo» (BO Tiendas)
// ---------------------------------------------------------------------
// Llamadas entrantes de la línea del ticket entre el día del ticket y
// los 3 días siguientes, y si su motivo es el mismo o diferente
// (Consultas/bo_tiendas_motivo.bqsql).
// Como en Tickets, todo llega ya calculado y escrito: la vista solo pinta.
// =====================================================================

namespace BO_Tienda.Models

open System

/// Filtros de la pestaña, en la URL como los de Tickets. Los que se llaman
/// igual (Desde, Hasta, Supervisor, Team, Agente, Marca) viajan de una
/// pestaña a la otra.
type FiltrosMotivo() =
    member val Desde = Nullable<DateOnly>() with get, set
    member val Hasta = Nullable<DateOnly>() with get, set
    member val Supervisor: string = null with get, set
    member val Team: string = null with get, set
    member val Agente: string = null with get, set
    member val Marca: string = null with get, set
    member val TipoTicket: string = null with get, set
    member val Motivo2: string = null with get, set
    member val Motivo3: string = null with get, set
    member val TipoMotivo: string = null with get, set    // Mismo motivo | Diferente motivo | vacío = los dos
    member val Ver = "motivo2" with get, set              // motivo2 | motivo3 | agente | team (filas del ranking)
    member val PeriodoMotivo = "semana" with get, set     // semana | mes | dia (gráfica por tiempo)
    // Lo que se elige al pulsar la gráfica por tiempo (las filas del ranking fijan
    // Motivo2, Motivo3, Agente o Team, que también son listas del panel)
    member val PeriodoLlamadas = Nullable<DateOnly>() with get, set   // primer día del punto elegido

/// Una columna del ranking. Orden = numero | texto (site.js ordena al pulsar).
/// Sentido (guía 5): "1" más es mejor, "-1" más es peor (semáforo pastel),
/// "0" o null sin color. Calor = la intensidad del acento sigue al volumen.
type ColumnaRanking =
    { Titulo: string
      Orden: string
      Sentido: string
      Calor: bool }

/// Una celda: texto ya escrito y el valor crudo para ordenar y colorear.
type CeldaRanking =
    { Texto: string
      Valor: string         // null = sin valor
      Clase: string }       // null = texto normal; si no, va en una pastilla con esa clase

/// Una fila del ranking. Pocas = menos de 20 llamadas: va al final y en gris.
/// Al pulsarla se filtra por su Clave (null = «(sin dato)», no se puede filtrar).
type FilaRanking =
    { Nombre: string
      Detalle: string       // texto pequeño bajo el nombre (null = sin detalle)
      Celdas: CeldaRanking[]
      Pocas: bool
      Clave: string
      Elegida: bool }

type Ranking =
    { Agrupacion: string    // encabezado de la primera columna
      Filtro: string        // parámetro de la URL que fija una fila (Motivo2 | Motivo3 | Agente | Team)
      Columnas: ColumnaRanking[]
      Filas: FilaRanking[]
      Total: CeldaRanking[] }

    static member Vacio = { Agrupacion = ""; Filtro = ""; Columnas = [||]; Filas = [||]; Total = [||] }

/// Todo lo que necesita Views/Informes/Motivo.cshtml.
type MotivoViewModel =
    { Config: InformeConfig
      Filtros: FiltrosMotivo
      CargadoEn: DateTime
      DuracionCarga: TimeSpan
      Error: string                       // null = sin error

      FechaMin: DateOnly                  // límites del calendario
      FechaMax: DateOnly
      Listas: FiltroLista[]
      MasListas: FiltroLista[]
      MasFiltrosActivos: int
      Selecciones: Seleccion[]            // lo elegido al pulsar la gráfica o el ranking (etiquetas que se quitan)

      HayDatos: bool
      SinAgente: int                      // llamadas de tickets que no cruzan con la nómina
      VentanaAbierta: bool                // hay tickets de los 3 últimos días: aún pueden recibir llamadas

      PeriodoAnterior: string
      HayPeriodoAnterior: bool
      Indicadores: TarjetaIndicador[]
      Ranking: Ranking
      Motivos: GraficaCrono               // % mismo / diferente motivo por semana / mes / día

      /// Filas (en la tabla de la consulta) que exporta «Descarga general».
      FilasDetalle: int[] }

    member this.Pagina =
        { PaginaInforme.Config = this.Config
          Activa = "motivo"
          CargadoEn = this.CargadoEn
          DuracionCarga = this.DuracionCarga
          Error = this.Error }

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
          HayDatos = false
          SinAgente = 0
          VentanaAbierta = false
          PeriodoAnterior = ""
          HayPeriodoAnterior = false
          Indicadores = [||]
          Ranking = Ranking.Vacio
          Motivos = GraficaCrono.Vacia
          FilasDetalle = [||] }
