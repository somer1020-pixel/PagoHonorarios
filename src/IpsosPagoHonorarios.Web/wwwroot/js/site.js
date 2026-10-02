// Cuenta regresiva en vivo (barra superior y bandas de plazo) y utilidades de formulario.
(function () {
  function fmt(ms) {
    const s = Math.ceil(Math.max(0, ms) / 1000);
    const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;
    const mm = String(m).padStart(2, '0') + ':' + String(r).padStart(2, '0');
    return h > 0 ? h + ':' + mm : mm;
  }
  function tick() {
    document.querySelectorAll('[data-vence]').forEach(el => {
      const ms = new Date(el.dataset.vence).getTime() - Date.now();
      const out = el.querySelector('[data-cd]') || el;
      if (ms <= 0) {
        out.textContent = el.dataset.vencidoTexto || 'Plazo vencido';
        el.classList.add('vencido');
        if (el.dataset.vencidoClase) el.className = el.dataset.vencidoClase;
      } else {
        out.textContent = (el.dataset.prefijo || '') + fmt(ms);
      }
    });
  }
  tick();
  setInterval(tick, 1000);

  // Muestra el nombre del archivo elegido en las zonas de carga.
  document.querySelectorAll('.zona-carga input[type=file]').forEach(inp => {
    inp.addEventListener('change', () => {
      const z = inp.closest('.zona-carga');
      let n = z.querySelector('.nombre-archivo');
      if (!n) { n = document.createElement('span'); n.className = 'nombre-archivo'; z.appendChild(n); }
      n.textContent = inp.files.length ? inp.files[0].name : '';
    });
  });

  // Validación de RUT en vivo (módulo 11).
  function dv(cuerpo) {
    let s = 0, m = 2;
    for (let i = cuerpo.length - 1; i >= 0; i--) { s += (+cuerpo[i]) * m; m = m === 7 ? 2 : m + 1; }
    const r = 11 - (s % 11);
    return r === 11 ? '0' : r === 10 ? 'K' : String(r);
  }
  document.querySelectorAll('[data-rut-vivo]').forEach(inp => {
    const badge = document.getElementById(inp.dataset.rutVivo);
    const check = () => {
      const c = inp.value.replace(/[.\s]/g, '').toUpperCase();
      const m = c.match(/^(\d{1,8})-?([\dK])$/);
      let t, tono;
      if (!m) { t = 'Formato inválido'; tono = 'danger'; }
      else if (dv(m[1]) === m[2]) { t = 'RUT válido · ' + m[1].replace(/\B(?=(\d{3})+(?!\d))/g, '.') + '-' + m[2]; tono = 'success'; }
      else { t = 'Dígito verificador incorrecto'; tono = 'danger'; }
      badge.textContent = t;
      badge.className = 'tag tag-' + tono;
      inp.setAttribute('aria-invalid', tono === 'danger' ? 'true' : 'false');
    };
    inp.addEventListener('input', check);
    check();
  });

  // Copiar al portapapeles.
  document.querySelectorAll('[data-copiar]').forEach(b => b.addEventListener('click', () => {
    const el = document.getElementById(b.dataset.copiar);
    navigator.clipboard?.writeText(el.value || el.textContent).then(() => { b.textContent = 'Copiado'; });
  }));
})();
