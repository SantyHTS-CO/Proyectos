# BO Tiendas — Informes (ASP.NET Core MVC en F#)

Portada **Informes** → tablero **BO Tiendas** (tickets Edison Shop + nómina).
Los datos salen de **BigQuery por ODBC**, se guardan en caché 12 h y se
recargan con «Actualizar».

Es la versión F# de `BO Tiendas` (C#): mismas rutas, misma consulta y mismos
resultados, con el aspecto de la **guía de estilos SOLARIS · GAIA**
(`guia-de-estilos.md`).

## Pestañas

| Pestaña | Ruta | Qué muestra |
|---|---|---|
| **Tickets** | `/Informes/Ver/bo-tiendas` | Tickets por agente/team y por día, FIR y tiempos, gráficas |
| **Motivo** | `/Informes/Motivo/bo-tiendas` | Llamadas de la línea del ticket en los 3 días siguientes y su motivo: 5 tarjetas (tickets, tickets que volvieron a llamar, llamadas de esos tickets, % mismo motivo, % diferente motivo), ranking por motivo, agente o team y el tipo de motivo en el tiempo |

Cada pestaña tiene su consulta, su caché y su botón «Actualizar». Al pasar de
una a otra se conservan los filtros comunes (fechas, supervisor, team, agente,
marca). «Motivo» cruza cada llamada con la pestaña Tickets por `id_ticket`
(para el agente y los tickets del periodo), así que usa las dos consultas.

## Cómo encaja

```
Navegador ──► HomeController (portada "Informes")
          └─► InformesController (pestañas, Actualizar, Descarga general)
                        │
                 InformeService  ← caché 12 h por pestaña, lee Consultas/*.bqsql
                        │
                 BigQueryOdbcFuente  (System.Data.Odbc + DSN BQCOL)
                        │
                 TableroTickets / TableroMotivo  ← filtros, indicadores, gráficas, ranking, Excel
                 (piezas comunes en Services/Tablero.fs)
```

## Dónde está cada cosa

| Quieres cambiar… | Archivo |
|---|---|
| La consulta de tickets | `Consultas/bo_tiendas.bqsql` (pulsa **Actualizar** en la página; no hace falta reiniciar) |
| La consulta de motivo | `Consultas/bo_tiendas_motivo.bqsql` (`appsettings.json` › `ConsultaMotivo`) |
| Cálculos de la pestaña Motivo (las 5 tarjetas, ranking, gráfica) | `Services/TableroMotivo.fs` |
| La tabla de ranking (semáforo, calor, paginación) | `Views/Shared/_Ranking.cshtml` + `wwwroot/js/site.js` |
| Título, descripción o marca de la tarjeta | `appsettings.json` › `Informes` (`"Marca"`: `yoigo` u `orange`) |
| La conexión (DSN) | `appsettings.json` › `ConnectionStrings:BigQueryOdbc` |
| Qué columna usa cada filtro, cálculos | `Services/TableroTickets.fs` (bloque «Columnas que usa el tablero») |
| Cómo se escriben las cifras (es-ES, «41,58 %») | `Infraestructura/Formato.fs` |
| Iconos de trazo y logotipo | `Infraestructura/Iconos.fs` |
| La gráfica por tiempo (SVG) | `Infraestructura/Graficas.fs` + `Views/Shared/_GraficaLineas.cshtml` |
| Cómo se ve el tablero (marcado) | `Views/Informes/Tickets.cshtml` |
| Colores, tamaños, modo claro/oscuro | `wwwroot/css/site.css` (variables al principio) |
| Filtros, fichas, tablas que se ordenan, animaciones | `wwwroot/js/site.js` |
| Tema claro / oscuro / automático | `wwwroot/js/tema.js` |

## Guía de estilos

Sigue `guia-de-estilos.md` (SOLARIS · GAIA). Lo principal:

- **Una sola hoja** (`site.css`), como CDM, con los nombres de variable de las
  pantallas de voz (`--acento`, `--texto`, `--superficie`, `--borde`, `--thead`…).
  **Nada de colores escritos a mano** en vistas ni scripts: todo sale de una variable.
- **Marca** en `<html data-marca="…">`: el informe usa la de `appsettings.json`
  (BO Tiendas es YGMM → `yoigo`, morado); portada y error van en marino y grises.
- **Letra de Windows** (Segoe UI Variable), sin Google Fonts ni CDN.
- **Cabecera** pegajosa de 72 px con línea de 3 px de la marca; **tablero** de
  1.120 a 1.720 px con el panel de filtros de 268 px; un solo punto de ruptura,
  760 px. Desplegables con menos de dos opciones se ocultan; Estado, Motivo y
  SubMotivo van en «Más filtros».
- **Vistas solo con marcado**: sin `<style>`, `<script>` ni `onclick`. El
  comportamiento está en `site.js` y lo calculado llega listo desde F#.
- **Movimiento** con los tiempos de la guía y nada si el sistema pide «reducir
  movimiento». Al filtrar no se repiten las animaciones de entrada.
- Probar siempre **claro y oscuro, y a 375 px**.

## F# y las vistas Razor

- **El orden de los archivos importa.** En `BO Tienda.fsproj` cada `.fs` solo ve
  lo que está *arriba* de él (Models → Services → Controllers → Program). Un
  archivo nuevo se añade en `<Compile Include>` en su sitio.
- **Las vistas `.cshtml` siguen siendo C#.** En un proyecto F# se compilan al
  ejecutar la app (paquete `Razor.RuntimeCompilation`), así que al editar una
  vista basta con recargar la página. Un error en una vista aparece al abrirla,
  no al compilar.
- `<LangVersion>latest</LangVersion>` en el `.fsproj` es necesario: sin él las
  vistas se compilan como C# 8 y fallan (`is not null`, `new()`…).
- Los modelos que usan las vistas exponen arrays (en la vista: `.Length`, no
  `.Count`) y `null` para "sin valor" (p. ej. `Model.Error is null`), que es lo
  que C# entiende directo. Dentro de F# se usan `option`, `Result` y listas.

## BigQuery (ODBC)

- Requiere el driver *Simba ODBC Driver for Google BigQuery* de **64 bits**.
- `ConnectionStrings:BigQueryOdbc` = `DSN=BQCOL` (configurado en
  «Orígenes de datos ODBC (64 bits)»). El DSN define el proyecto de facturación
  (*Catalog*) y el login de Google.
- Si la consulta tiene `?`, la app lo llena con la fecha de hoy.
- Si existe un DSN de **usuario** y uno de **sistema** con el mismo nombre, gana el
  de usuario. En un servidor/IIS solo se ve el de sistema.
- Para el servidor: usa una *Service Account* (roles **BigQuery Data Viewer** y
  **BigQuery Job User**) en el DSN de sistema, no tu cuenta personal.
- Si BigQuery corta la conexión (pasa a ratos por el proxy), la app reintenta
  hasta 3 veces antes de mostrar el error.

> Si BigQuery no responde, la app **no se cae**: el tablero muestra el error en rojo.

## Consultas: ¿por qué `.bqsql`?

Con `.sql`, Visual Studio revisa el archivo como **SQL Server** y marca errores
falsos (SQL80001) con sintaxis de BigQuery (comillas invertidas, `QUALIFY`,
`SAFE_OFFSET`…). `.bqsql` evita eso. `.sql` también funciona.

La consulta no tiene `ORDER BY`, así que BigQuery puede devolver las filas en
otro orden en cada carga: el tablero no cambia (todo va agrupado y ordenado),
pero el orden de las filas de la «Descarga general» sí puede variar.

## Agregar otro informe

Agrega un objeto en `"Informes"` de `appsettings.json` y su consulta en
`Consultas/`. Se pinta con el mismo tablero de tickets, así que la consulta debe
traer las columnas que usa `TableroTickets.fs`.

## Siguientes pasos recomendados

1. **Login y permisos** (la tarjeta "Sin acceso" real): Windows Authentication
   si es intranet o Microsoft Entra ID; luego `[<Authorize(Roles = "...")>]`.
2. **Secretos**: mueve cadenas de conexión a *User Secrets* / variables de entorno.
3. **Publicar**: IIS en un servidor interno o Azure App Service.
