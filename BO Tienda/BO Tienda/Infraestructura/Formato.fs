// =====================================================================
// Infraestructura/Formato.fs — Cómo se escriben las cifras
// ---------------------------------------------------------------------
// Guía de estilos (1. Tipo de letra): cultura es-ES, dos decimales y «%»
// separado por un espacio fino que no se parte (U+202F): «41,58 %».
// Lo usan el tablero (F#) y las vistas (@Formato.Entero(...)).
// =====================================================================

module BO_Tienda.Infraestructura.Formato

open System
open System.Globalization

/// Cultura de las cifras y de los nombres de mes.
let Es = CultureInfo.GetCultureInfo "es-ES"

/// Espacio fino que no se parte, entre la cifra y su unidad.
let private espacioFino = " "

/// 1234 -> "1.234"
let Entero (n: int) = n.ToString("N0", Es)

/// Cifra sin decimales que llega como float (volúmenes del periodo).
let Numero (v: float) = v.ToString("N0", Es)

/// Fracción 0–1 -> "41,58 %"
let Porcentaje (fraccion: float) = (fraccion * 100.0).ToString("N2", Es) + espacioFino + "%"

/// Fracción 0–1 sin decimales, para el eje de las gráficas: 0.4 -> "40 %"
let PorcentajeEje (fraccion: float) = (fraccion * 100.0).ToString("N0", Es) + espacioFino + "%"

/// Cifra con dos decimales: 1.3245 -> "1,32"
let Decimal (v: float) = v.ToString("N2", Es)

/// Puntos porcentuales: 1.25 -> "1,25 pp"
let Puntos (puntos: float) = puntos.ToString("N2", Es) + espacioFino + "pp"

/// Celda de matriz: vacía si es 0 (como en Power BI).
let Celda (n: int) = if n = 0 then "" else Entero n

/// Horas -> "31h 06m" (como las tarjetas del Power BI).
let Duracion (horas: float) =
    let minutos = int (Math.Round(horas * 60.0))
    $"""{minutos / 60}h {(minutos % 60).ToString "00"}m"""

/// Números para atributos SVG/CSS y data-valor: siempre con punto decimal.
let Invariante (v: float) = v.ToString("0.##", CultureInfo.InvariantCulture)
