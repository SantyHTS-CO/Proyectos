// =====================================================================
// Infraestructura/Excel.fs — «Descarga general» a Excel (ClosedXML)
// ---------------------------------------------------------------------
// Exporta las columnas de la consulta tal cual (menos las ocultas), solo
// de las filas que pasan los filtros. Lo usan las dos pestañas.
// =====================================================================

module BO_Tienda.Infraestructura.Excel

open System
open System.IO
open ClosedXML.Excel
open BO_Tienda.Models

/// filas = posiciones en datos.Filas; ocultas = columnas que no salen (p. ej.
/// la llave del cruce); nombre = nombre de la hoja y de la tabla de Excel.
let exportar (datos: TablaDatos) (filas: int[]) (ocultas: string list) (nombre: string) : byte[] =
    use libro = new XLWorkbook()
    let hoja = libro.Worksheets.Add nombre

    // Posiciones de las columnas que sí se exportan
    let columnas =
        [ 0 .. datos.Columnas.Count - 1 ]
        |> List.filter (fun c ->
            ocultas |> List.forall (fun o -> not (o.Equals(datos.Columnas[c], StringComparison.OrdinalIgnoreCase))))

    columnas |> List.iteri (fun c col -> hoja.Cell(1, c + 1).Value <- XLCellValue.FromObject(datos.Columnas[col]))

    filas
    |> Array.iteri (fun n i ->
        let valores = datos.Filas[i]
        columnas |> List.iteri (fun c col -> hoja.Cell(n + 2, c + 1).Value <- XLCellValue.FromObject(valores[col])))

    // Formato de tabla de Excel (encabezado con filtros) + fila 1 fija
    hoja.Range(1, 1, max (filas.Length + 1) 2, columnas.Length).CreateTable(nombre) |> ignore
    hoja.SheetView.FreezeRows 1

    use flujo = new MemoryStream()
    libro.SaveAs flujo
    flujo.ToArray()
