// =====================================================================
// Models/Modelos.fs — Tipos que representan los datos
// ---------------------------------------------------------------------
// "Modelo" = un tipo con propiedades. La fuente llena estos tipos y las
// vistas los pintan. Mantenerlos simples.
// =====================================================================

namespace BO_Tienda.Models

open System
open System.Collections.Generic

/// Configuración de un informe (viene de la sección "Informes" de
/// appsettings.json). Cada informe se pinta como una tarjeta en la portada.
/// Es una clase con "get, set" (no un record) porque .NET la llena creando
/// el objeto vacío y asignando cada propiedad leída del JSON.
type InformeConfig() =
    member val Id = "" with get, set
    member val Titulo = "" with get, set
    member val Descripcion = "" with get, set
    member val Pie = "" with get, set
    member val Marca = "yoigo" with get, set       // yoigo | orange: colores del informe (guía de estilos, 2.1)
    member val Fuente = "BigQuery" with get, set   // Nombre de la fuente (hoy solo BigQuery por ODBC)
    member val Consulta = "" with get, set         // SQL en una línea o un archivo "Consultas/x.bqsql"
    member val ConsultaMotivo = "" with get, set   // vacío = el informe no tiene la pestaña «Motivo»
    member val Habilitado = true with get, set     // false => tarjeta "Sin acceso"

    member this.TieneMotivo = not (String.IsNullOrWhiteSpace this.ConsultaMotivo)

/// Resultado cacheado de un informe: los datos + cuándo se cargaron y
/// cuánto tardó la consulta (para el texto "cargado a las 10:14 en 8 s").
type Cargado<'T> =
    { Datos: 'T
      CargadoEn: DateTime
      Duracion: TimeSpan }

/// Resultado "crudo" de una consulta: nombres de columna + filas, sin
/// importar cuántas columnas traiga. Lo usan el tablero de tickets y la
/// descarga a Excel (que exporta todas las columnas).
type TablaDatos() =
    member val Columnas = List<string>()
    member val Filas = List<obj[]>()   // un valor null = celda vacía

    /// Posición de una columna por nombre (sin distinguir mayúsculas); -1 si no está.
    member this.Indice(columna: string) =
        this.Columnas.FindIndex(fun c -> c.Equals(columna, StringComparison.OrdinalIgnoreCase))
