// =====================================================================
// wwwroot/js/site.js — Comportamiento de las pantallas (sin librerías)
// ---------------------------------------------------------------------
// Las vistas solo llevan marcado: todo el JavaScript vive aquí.
//   1. Barra de carga arriba al navegar o aplicar filtros
//   2. Filtros: cada cambio envía el formulario; [data-fijar] = botones,
//      barras, filas del ranking y etiquetas que fijan (o quitan) un filtro
//   3. «Actualizar»: «Actualizando…» mientras BigQuery responde
//   4. Árbol desplegable (Estado de tickets)
//   5. Tablas que se ordenan (table.ranking)
//   6. Fichas al pasar el ratón por las gráficas, con guía vertical; clic
//      en un punto = filtrar por ese periodo
//   7. Aparición escalonada de tarjetas y cifras que cuentan
// Con «reducir movimiento» no se anima nada.
// =====================================================================
(function () {
    'use strict';

    var CLAVE_CIFRAS = 'bo-tiendas-cifras';   // la misma que en tema.js
    var DURACION_CUENTA = 780;                // ms, como --t-cuenta
    var sinMovimiento = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // ------------------------------------------------------------------
    // 1. Barra de carga
    // ------------------------------------------------------------------
    var barra = null;

    function mostrarCarga() {
        if (!barra) {
            barra = document.createElement('div');
            barra.className = 'barra-carga';
            barra.setAttribute('aria-hidden', 'true');
            document.body.appendChild(barra);
        }
        barra.classList.remove('activa');
        void barra.offsetWidth;               // reinicia la animación
        barra.classList.add('activa');
    }

    function ocultarCarga() {
        if (barra) barra.classList.remove('activa');
    }

    document.addEventListener('click', function (e) {
        var enlace = e.target.closest('a[href]');
        if (!enlace || e.defaultPrevented || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
        if (enlace.target === '_blank' || enlace.hasAttribute('download') || enlace.hasAttribute('data-sin-carga')) return;
        if (enlace.origin !== location.origin) return;
        if (enlace.hasAttribute('data-filtrar')) guardarCifras();
        mostrarCarga();
    });

    // ------------------------------------------------------------------
    // 2. Filtros
    // ------------------------------------------------------------------
    var filtros = document.querySelector('form[data-filtros]');

    // Las cifras de la tira antes de filtrar: la página nueva las hace
    // pasar del valor anterior al nuevo (y tema.js ve la señal).
    function guardarCifras() {
        var valores = {};
        document.querySelectorAll('[data-cifra][data-valor]').forEach(function (el) {
            valores[el.dataset.clave] = el.dataset.valor;
        });
        try { sessionStorage.setItem(CLAVE_CIFRAS, JSON.stringify(valores)); } catch (e) { }
    }

    // Lo vacío o igual a su valor por defecto no va en la URL: así un
    // enlace guardado sigue al último día con datos.
    function prepararEnvio(form) {
        form.querySelectorAll('input[name], select[name]').forEach(function (campo) {
            var defecto = campo.dataset.defecto;
            if (campo.value === '' || (defecto !== undefined && campo.value === defecto)) campo.disabled = true;
        });
        guardarCifras();
        mostrarCarga();
    }

    function enviar(form) {
        prepararEnvio(form);
        form.submit();
    }

    if (filtros) {
        filtros.addEventListener('change', function (e) {
            if (e.target.matches('select, input[type="date"]')) enviar(filtros);
        });
        filtros.addEventListener('submit', function () { prepararEnvio(filtros); });
    }

    // Fija un valor del formulario y recarga: lo usan los botones (Team / Agente,
    // Suscripción, Día / Semana / Mes), las barras, las filas del ranking, los
    // puntos de las gráficas y las etiquetas de filtros elegidos.
    //   ancla    = bloque al que volver tras recargar
    //   limpiar  = otros campos que se vacían a la vez (nombres separados por espacio)
    // Un valor vacío quita el filtro. Si el campo no está en el panel (una lista
    // con una sola opción no se pinta) se crea oculto.
    function fijar(nombre, valor, ancla, limpiar) {
        if (!filtros || !nombre) return;
        var campo = filtros.elements[nombre];
        if (!campo) {
            campo = document.createElement('input');
            campo.type = 'hidden';
            campo.name = nombre;
            filtros.appendChild(campo);
        }
        campo.value = valor || '';
        if (limpiar) {
            limpiar.split(' ').forEach(function (otro) {
                if (filtros.elements[otro]) filtros.elements[otro].value = '';
            });
        }
        if (ancla) filtros.action = filtros.action.split('#')[0] + '#' + ancla;
        enviar(filtros);
    }

    document.addEventListener('click', function (e) {
        var boton = e.target.closest('[data-fijar]');
        if (!boton || boton.disabled) return;
        fijar(boton.dataset.fijar, boton.dataset.valor, boton.dataset.ancla, boton.dataset.limpiar);
    });

    // Las filas del ranking que filtran se alcanzan con el teclado
    document.addEventListener('keydown', function (e) {
        if ((e.key === 'Enter' || e.key === ' ') && e.target.matches('tr.elegible')) {
            e.preventDefault();
            e.target.click();
        }
    });

    // Al volver con «Atrás» la página puede salir de la caché del navegador
    window.addEventListener('pageshow', function () {
        ocultarCarga();
        if (filtros) filtros.querySelectorAll(':disabled').forEach(function (campo) { campo.disabled = false; });
    });

    // ------------------------------------------------------------------
    // 3. «Actualizar»
    // ------------------------------------------------------------------
    document.querySelectorAll('form[data-actualizar]').forEach(function (form) {
        form.addEventListener('submit', function () {
            var boton = form.querySelector('button');
            var texto = boton.querySelector('span');
            if (texto) texto.textContent = 'Actualizando…';
            boton.disabled = true;
            guardarCifras();
            mostrarCarga();
        });
    });

    // ------------------------------------------------------------------
    // 4. Árbol desplegable: cada periodo muestra sus filas por agente o team
    // ------------------------------------------------------------------
    document.querySelectorAll('[data-desplegar]').forEach(function (boton) {
        boton.addEventListener('click', function () {
            var abrir = boton.getAttribute('aria-expanded') !== 'true';
            boton.setAttribute('aria-expanded', String(abrir));
            boton.closest('table')
                .querySelectorAll('tr.hija[data-padre="' + boton.dataset.desplegar + '"]')
                .forEach(function (fila) { fila.hidden = !abrir; });
        });
    });

    // ------------------------------------------------------------------
    // 5. Rankings (table.ranking)
    //    - Se ordenan: th[data-orden="numero|texto"] y td[data-valor].
    //      Primer clic: números de mayor a menor, textos de la A a la Z.
    //    - [data-mapa]: semáforo pastel por tercios según th[data-sentido]
    //      (1 más es mejor, -1 más es peor) y calor en th[data-calor="si"].
    //    - [data-paginar="10"]: de 10 en 10, con «1–10 / 57» y flechas.
    //    Las filas .pocas siempre van al final y sin color; la de totales
    //    (tfoot) no se mueve ni se colorea.
    // ------------------------------------------------------------------
    var alfabetico = new Intl.Collator('es', { numeric: true, sensitivity: 'base' });

    function valorDe(celda) { return parseFloat(celda.dataset.valor); }

    function colorear(tabla) {
        var filas = Array.prototype.filter.call(tabla.tBodies[0].rows, function (fila) {
            return !fila.classList.contains('pocas');
        });

        tabla.querySelectorAll('thead th').forEach(function (th) {
            var celdas = filas
                .map(function (fila) { return fila.cells[th.cellIndex]; })
                .filter(function (celda) { return celda && isFinite(valorDe(celda)); });
            if (!celdas.length) return;

            if (th.dataset.calor === 'si') {
                var mayor = Math.max.apply(null, celdas.map(valorDe));
                celdas.forEach(function (celda) {
                    celda.classList.add('calor');
                    celda.style.setProperty('--calor', mayor > 0 ? (valorDe(celda) / mayor).toFixed(3) : '0');
                });
            }

            var sentido = parseInt(th.dataset.sentido, 10);
            if ((sentido === 1 || sentido === -1) && celdas.length >= 3) {
                var orden = celdas.map(valorDe).sort(function (a, b) { return a - b; });
                var bajo = orden[Math.floor(orden.length / 3)];
                var alto = orden[Math.floor(orden.length * 2 / 3)];
                celdas.forEach(function (celda) {
                    var v = valorDe(celda);
                    var tramo = v < bajo ? 0 : v < alto ? 1 : 2;      // 0 bajo, 2 alto
                    if (sentido === -1) tramo = 2 - tramo;            // más es peor: alto = rojo
                    celda.classList.add(['mapa-rojo', 'mapa-ambar', 'mapa-verde'][tramo]);
                });
            }
        });
    }

    function paginar(tabla) {
        var porPagina = parseInt(tabla.dataset.paginar, 10);
        if (!porPagina) return null;

        var cuerpo = tabla.tBodies[0];
        // Empieza en la página de la fila elegida (si la hay), para que se vea
        var primeraElegida = Array.prototype.findIndex.call(cuerpo.rows, function (fila) {
            return fila.classList.contains('elegida');
        });
        var pagina = primeraElegida > 0 ? Math.floor(primeraElegida / porPagina) : 0;
        var barra = document.createElement('div');
        var texto = document.createElement('span');
        var anterior = document.createElement('button');
        var siguiente = document.createElement('button');
        barra.className = 'paginador';
        anterior.type = siguiente.type = 'button';
        anterior.className = 'anterior';
        siguiente.className = 'siguiente';
        anterior.setAttribute('aria-label', 'Página anterior');
        siguiente.setAttribute('aria-label', 'Página siguiente');
        barra.append(texto, anterior, siguiente);
        (tabla.closest('.tabla-scroll') || tabla).after(barra);

        function pintar() {
            var filas = cuerpo.rows;
            var paginas = Math.max(1, Math.ceil(filas.length / porPagina));
            pagina = Math.min(pagina, paginas - 1);
            for (var i = 0; i < filas.length; i++) {
                filas[i].classList.toggle('fuera-de-pagina', Math.floor(i / porPagina) !== pagina);
            }
            texto.textContent = (pagina * porPagina + 1) + '–' + Math.min(filas.length, (pagina + 1) * porPagina) + ' / ' + filas.length;
            anterior.disabled = pagina === 0;
            siguiente.disabled = pagina >= paginas - 1;
            barra.hidden = filas.length <= porPagina;
        }

        anterior.addEventListener('click', function () { pagina--; pintar(); });
        siguiente.addEventListener('click', function () { pagina++; pintar(); });
        pintar();

        // Tras ordenar se vuelve a la primera página
        return function () { pagina = 0; pintar(); };
    }

    document.querySelectorAll('table.ranking').forEach(function (tabla) {
        var cuerpo = tabla.tBodies[0];
        if (tabla.hasAttribute('data-mapa')) colorear(tabla);
        var volverAPaginar = null;

        tabla.querySelectorAll('thead th[data-orden]').forEach(function (th) {
            var boton = document.createElement('button');
            boton.type = 'button';
            boton.className = 'ordenar';
            while (th.firstChild) boton.appendChild(th.firstChild);
            th.appendChild(boton);

            boton.addEventListener('click', function () {
                var numerica = th.dataset.orden === 'numero';
                var anterior = th.getAttribute('aria-sort');
                var ascendente = anterior ? anterior === 'descending' : !numerica;
                var columna = th.cellIndex;

                tabla.querySelectorAll('thead th[aria-sort]').forEach(function (otro) { otro.removeAttribute('aria-sort'); });
                th.setAttribute('aria-sort', ascendente ? 'ascending' : 'descending');

                Array.prototype.slice.call(cuerpo.rows)
                    .sort(function (a, b) {
                        var pocasA = a.classList.contains('pocas'), pocasB = b.classList.contains('pocas');
                        if (pocasA !== pocasB) return pocasA ? 1 : -1;   // las de pocas, siempre al final
                        var ca = a.cells[columna], cb = b.cells[columna];
                        var r = numerica
                            ? parseFloat(ca.dataset.valor || '0') - parseFloat(cb.dataset.valor || '0')
                            : alfabetico.compare(ca.textContent.trim(), cb.textContent.trim());
                        return ascendente ? r : -r;
                    })
                    .forEach(function (fila) { cuerpo.appendChild(fila); });

                if (volverAPaginar) volverAPaginar();
            });
        });

        volverAPaginar = paginar(tabla);
    });

    // ------------------------------------------------------------------
    // 6. Fichas en las gráficas por tiempo: el <title> de cada punto
    //    («Etiqueta · Serie valor · …») se pinta como ficha junto al punto
    //    más cercano al ratón, con una guía vertical; los demás se desvanecen.
    //    Si la gráfica filtra (data-elegir = parámetro de la URL), pulsar cerca
    //    de un punto elige ese periodo; si ya es el elegido, lo quita. El
    //    elegido (.elegida) queda marcado con una guía fija.
    // ------------------------------------------------------------------
    document.querySelectorAll('[data-crono]').forEach(function (lienzo) {
        var marcas = Array.prototype.slice.call(lienzo.querySelectorAll('.marca-x'));
        if (!marcas.length) return;

        // El <title> pasa a data-ficha para que no salgan dos fichas
        marcas.forEach(function (marca) {
            var titulo = marca.querySelector('title');
            marca.dataset.ficha = titulo ? titulo.textContent : '';
            marca.setAttribute('aria-label', marca.dataset.ficha);
            if (titulo) titulo.remove();
        });

        var posiciones = marcas.map(function (marca) { return parseFloat(marca.dataset.x); });
        var guia = document.createElement('div');
        var ficha = document.createElement('div');
        guia.className = 'guia';
        ficha.className = 'ficha';
        guia.hidden = ficha.hidden = true;
        ficha.setAttribute('role', 'tooltip');
        lienzo.appendChild(guia);
        lienzo.appendChild(ficha);

        var activa = null;

        function mostrar(i) {
            if (activa === marcas[i]) return;
            if (activa) activa.classList.remove('foco');
            activa = marcas[i];
            activa.classList.add('foco');
            lienzo.classList.add('con-foco');

            var partes = activa.dataset.ficha.split(' · ');
            ficha.textContent = '';
            var titulo = document.createElement('strong');
            titulo.textContent = partes[0];
            ficha.appendChild(titulo);
            partes.slice(1).forEach(function (parte) {
                var linea = document.createElement('span');
                linea.textContent = parte;
                ficha.appendChild(linea);
            });

            var x = lienzo.clientWidth * posiciones[i] / 100;
            guia.style.left = x + 'px';
            guia.hidden = ficha.hidden = false;
            // A la derecha del punto; a la izquierda si no cabe
            var izquierda = x + 12 + ficha.offsetWidth > lienzo.clientWidth ? x - 12 - ficha.offsetWidth : x + 12;
            ficha.style.left = Math.max(0, izquierda) + 'px';
        }

        function ocultar() {
            if (activa) activa.classList.remove('foco');
            activa = null;
            lienzo.classList.remove('con-foco');
            guia.hidden = ficha.hidden = true;
        }

        // El punto más cercano al ratón (x en % del ancho)
        function cercanaA(e) {
            var caja = lienzo.getBoundingClientRect();
            var x = (e.clientX - caja.left) / caja.width * 100;
            var cercana = 0;
            posiciones.forEach(function (p, i) {
                if (Math.abs(p - x) < Math.abs(posiciones[cercana] - x)) cercana = i;
            });
            return cercana;
        }

        lienzo.addEventListener('pointermove', function (e) { mostrar(cercanaA(e)); });
        lienzo.addEventListener('pointerleave', ocultar);

        var parametro = lienzo.dataset.elegir;
        if (!parametro) return;

        function elegir(i) {
            var marca = marcas[i];
            var tarjeta = lienzo.closest('[id]');
            fijar(parametro, marca.classList.contains('elegida') ? '' : marca.dataset.valor, tarjeta ? tarjeta.id : null);
        }

        lienzo.classList.add('elegible');
        lienzo.addEventListener('click', function (e) { elegir(cercanaA(e)); });
        marcas.forEach(function (marca, i) {
            marca.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    elegir(i);
                }
            });
        });

        var elegida = marcas.filter(function (marca) { return marca.classList.contains('elegida'); })[0];
        if (elegida) {
            var fija = document.createElement('div');
            fija.className = 'guia fija';
            fija.style.left = elegida.dataset.x + '%';
            lienzo.appendChild(fija);
            lienzo.classList.add('con-seleccion');
        }
    });

    // ------------------------------------------------------------------
    // 7. Movimiento: tarjetas escalonadas (45 ms entre hermanas) y cifras
    //    que cuentan (780 ms). Si se viene de filtrar, las cifras pasan del
    //    valor anterior al nuevo y no se repite la entrada (tema.js).
    // ------------------------------------------------------------------
    document.querySelectorAll('.aparece').forEach(function (el, i) {
        el.style.setProperty('--i', Math.min(i, 12));
    });

    var anteriores = null;
    try {
        anteriores = JSON.parse(sessionStorage.getItem(CLAVE_CIFRAS) || 'null');
        sessionStorage.removeItem(CLAVE_CIFRAS);
    } catch (e) { }

    var escribir = {
        entero: function (v) {
            return Math.round(v).toLocaleString('es-ES', { useGrouping: 'always' });
        },
        porcentaje: function (v) {
            return v.toLocaleString('es-ES', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' %';
        },
        duracion: function (horas) {
            var minutos = Math.round(horas * 60);
            return Math.floor(minutos / 60) + 'h ' + String(minutos % 60).padStart(2, '0') + 'm';
        },
        decimal: function (v) {
            return v.toLocaleString('es-ES', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }
    };

    // Aproxima cubic-bezier(.22, .61, .36, 1): arranca rápido y frena sin rebote
    function suavizar(t) { return 1 - Math.pow(1 - t, 3); }

    // En una pestaña oculta el navegador pausa las animaciones: ahí no se cuenta
    if (!sinMovimiento && !document.hidden) {
        document.querySelectorAll('[data-cifra][data-valor]').forEach(function (el) {
            var destino = parseFloat(el.dataset.valor);
            var formato = escribir[el.dataset.cifra];
            var previo = anteriores && el.dataset.clave in anteriores ? parseFloat(anteriores[el.dataset.clave]) : null;
            var desde = previo !== null && isFinite(previo) ? previo : 0;
            if (!formato || !isFinite(destino) || desde === destino) return;
            if (anteriores && previo === null) return;   // tras filtrar, lo que no estaba antes no cuenta

            var final = el.textContent;               // el texto del servidor, exacto
            var inicio = null;
            el.textContent = formato(desde);

            var terminado = false;

            function terminar() {
                terminado = true;
                el.textContent = final;
            }

            function paso(ahora) {
                if (terminado) return;
                if (inicio === null) inicio = ahora;
                var t = Math.min(1, (ahora - inicio) / DURACION_CUENTA);
                if (t < 1) {
                    el.textContent = formato(desde + (destino - desde) * suavizar(t));
                    requestAnimationFrame(paso);
                } else {
                    terminar();
                }
            }
            requestAnimationFrame(paso);
            // Por si la pestaña se oculta a mitad: la cifra final siempre queda
            setTimeout(terminar, DURACION_CUENTA + 150);
        });
    }
})();
