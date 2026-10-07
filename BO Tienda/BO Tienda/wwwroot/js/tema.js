// =====================================================================
// wwwroot/js/tema.js — Tema claro / oscuro
// ---------------------------------------------------------------------
// Va en <head>, antes del CSS, para que la página no parpadee al cargar.
//   data-tema="claro|oscuro" en <html>: lo que usan las variables de site.css
//   data-tema-modo="auto|claro|oscuro": lo que eligió la persona
//     (auto = el de Windows). Se guarda en este navegador.
// El botón [data-cambiar-tema] de la cabecera pasa de uno a otro.
//
// También marca <html> como «actualizado» si la página viene de aplicar
// un filtro o de «Actualizar» (site.js deja la señal antes de enviar):
// así no se repiten las animaciones de entrada.
// =====================================================================
(function () {
    'use strict';

    var CLAVE = 'bo-tiendas-tema';
    var CLAVE_CIFRAS = 'bo-tiendas-cifras';   // la misma que en site.js
    var MODOS = ['auto', 'claro', 'oscuro'];
    var NOMBRES = { auto: 'automático', claro: 'claro', oscuro: 'oscuro' };

    var raiz = document.documentElement;
    var oscuroSistema = window.matchMedia('(prefers-color-scheme: dark)');

    function leer() {
        try {
            var modo = localStorage.getItem(CLAVE);
            return MODOS.indexOf(modo) >= 0 ? modo : 'auto';
        } catch (e) {
            return 'auto';
        }
    }

    var actual = leer();

    function aplicar() {
        raiz.setAttribute('data-tema-modo', actual);
        raiz.setAttribute('data-tema', actual === 'auto' ? (oscuroSistema.matches ? 'oscuro' : 'claro') : actual);
    }

    aplicar();
    oscuroSistema.addEventListener('change', function () {
        if (actual === 'auto') aplicar();
    });

    try {
        if (sessionStorage.getItem(CLAVE_CIFRAS) !== null) raiz.classList.add('actualizado');
    } catch (e) { }

    document.addEventListener('DOMContentLoaded', function () {
        var boton = document.querySelector('[data-cambiar-tema]');
        if (!boton) return;

        function siguiente() {
            return MODOS[(MODOS.indexOf(actual) + 1) % MODOS.length];
        }

        function rotular() {
            var texto = 'Tema ' + NOMBRES[actual] + '. Pulsa para cambiar a ' + NOMBRES[siguiente()];
            boton.title = texto;
            boton.setAttribute('aria-label', texto);
        }

        rotular();
        boton.addEventListener('click', function () {
            actual = siguiente();
            try { localStorage.setItem(CLAVE, actual); } catch (e) { }
            aplicar();
            rotular();
        });
    });
})();
