// FIS Presents brand kit runtime: page chrome, staged reveals, counters, tabs and an ambient backdrop.
(() => {
  'use strict';
  const body = document.body;
  const demo = window.FIS_DEMO || {};
  const context = window.FIS_DEMO_CONTEXT || {};
  const page = Number(body.dataset.page || 1), pages = Number(body.dataset.pages || 1);

  // Persona data chosen in the Studio's start dialog: dotted path lookups such as "trade.price".
  function data(path, fallback) {
    let value = context.data;
    for (const key of String(path).split('.')) {
      if (value == null || typeof value !== 'object' || !(key in value)) return fallback;
      value = value[key];
    }
    return value ?? fallback;
  }

  function lookup(path) {
    if (path === 'clientName') return context.clientName || undefined;
    if (path === 'persona') return context.persona || undefined;
    if (path === 'dataSetName') return context.dataSetName || undefined;
    return data(path, undefined);
  }

  function personalise() {
    document.querySelectorAll('[data-fis-field]').forEach(element => {
      const value = lookup(element.dataset.fisField);
      if (value !== undefined && value !== null && value !== '') element.textContent = String(value);
    });
    document.querySelectorAll('[data-fis-count]').forEach(element => {
      const value = Number(lookup(element.dataset.fisCount));
      if (Number.isFinite(value)) element.dataset.count = String(value);
    });
    document.querySelectorAll('[data-fis-value]').forEach(element => {
      const value = lookup(element.dataset.fisValue);
      if (value !== undefined && value !== null) element.value = String(value);
    });
    document.querySelectorAll('[data-fis-when]').forEach(element => {
      element.hidden = !lookup(element.dataset.fisWhen);
    });
    const hero = document.querySelector('.hero');
    if (hero && (context.clientName || context.persona)) {
      const prepared = document.createElement('p');
      prepared.className = 'prepared reveal';
      const parts = [];
      if (context.clientName) parts.push(['Prepared for', context.clientName]);
      if (context.persona) parts.push(['Persona', context.persona]);
      parts.forEach(([label, value]) => {
        const item = document.createElement('span');
        const name = document.createElement('small'); name.textContent = label;
        const text = document.createElement('b'); text.textContent = value;
        item.append(name, text);
        prepared.append(item);
      });
      const lede = hero.querySelector('.lede');
      if (lede) lede.after(prepared); else hero.append(prepared);
    }
  }

  function chrome() {
    const header = document.createElement('header');
    header.className = 'topbar';
    header.innerHTML = '<img class="logo" src="shared/fis-logo.png" alt="FIS"><div class="crumb"><span class="demo-name"></span><span class="chapter"></span></div><div class="progress" aria-hidden="true"></div>';
    header.querySelector('.demo-name').textContent = body.dataset.demo || demo.name || 'FIS Presents';
    header.querySelector('.chapter').textContent = `${String(page).padStart(2, '0')} · ${body.dataset.chapter || ''}`;
    if (context.clientName) {
      const client = document.createElement('span');
      client.className = 'client-chip';
      client.textContent = context.clientName;
      client.title = context.persona ? `Prepared for ${context.clientName} · ${context.persona}` : `Prepared for ${context.clientName}`;
      header.querySelector('.crumb').after(client);
    }
    const progress = header.querySelector('.progress');
    for (let index = 1; index <= pages; index++) {
      const tick = document.createElement('i');
      if (index < page) tick.className = 'done';
      if (index === page) tick.className = 'current';
      progress.appendChild(tick);
    }
    body.prepend(header);
    const footer = document.createElement('footer');
    footer.className = 'foot';
    footer.innerHTML = '<span><span class="version"></span> <span class="solution"></span></span><span>Illustrative content · FIS pre-sales digital demonstration</span>';
    footer.querySelector('.version').textContent = demo.version ? `v${demo.version}` : 'Draft';
    footer.querySelector('.solution').textContent = demo.solution ? `· ${demo.business} › ${demo.solution}` : '';
    body.append(footer);
  }

  function reveal() {
    document.querySelectorAll('.reveal').forEach((element, index) => {
      const delay = Number(element.dataset.delay ?? index * 90);
      setTimeout(() => element.classList.add('in'), 120 + delay);
    });
  }

  function animateCount(element, target) {
    const decimals = Number(element.dataset.decimals || 0), suffix = element.dataset.suffix || '', prefix = element.dataset.prefix || '';
    const start = Number(element.dataset.current || 0), began = performance.now(), duration = Number(element.dataset.duration || 1400);
    const step = now => {
      const t = Math.min(1, (now - began) / duration), eased = 1 - Math.pow(1 - t, 3);
      const value = start + (target - start) * eased;
      element.textContent = prefix + value.toLocaleString(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }) + suffix;
      if (t < 1) requestAnimationFrame(step); else element.dataset.current = String(target);
    };
    requestAnimationFrame(step);
  }

  function counters() {
    document.querySelectorAll('[data-count]').forEach(element => setTimeout(() => animateCount(element, Number(element.dataset.count)), 350));
  }

  const tabListeners = [];
  function tabs() {
    document.querySelectorAll('[data-tabs]').forEach(group => {
      const select = key => {
        group.querySelectorAll('[data-tab]').forEach(button => button.setAttribute('aria-selected', String(button.dataset.tab === key)));
        group.querySelectorAll('[data-panel]').forEach(panel => { panel.hidden = panel.dataset.panel !== key; });
        tabListeners.forEach(listener => listener(group.dataset.tabs, key));
      };
      group.querySelectorAll('[data-tab]').forEach(button => button.addEventListener('click', () => select(button.dataset.tab)));
      const initial = group.querySelector('[data-tab][aria-selected="true"]') || group.querySelector('[data-tab]');
      if (initial) requestAnimationFrame(() => select(initial.dataset.tab));
    });
  }

  function backdrop() {
    const canvas = document.querySelector('canvas.backdrop');
    if (!canvas || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const context = canvas.getContext('2d');
    const color = getComputedStyle(body).getPropertyValue('--accent').trim() || '#4bcd3e';
    let width = 0, height = 0;
    const dots = Array.from({ length: 46 }, () => ({ x: Math.random(), y: Math.random(), vx: (Math.random() - .5) * .012, vy: (Math.random() - .5) * .012, r: 1 + Math.random() * 1.8 }));
    const resize = () => {
      const ratio = Math.min(devicePixelRatio || 1, 2);
      width = innerWidth; height = innerHeight;
      canvas.width = width * ratio; canvas.height = height * ratio;
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
    };
    addEventListener('resize', resize); resize();
    let last = performance.now();
    const frame = now => {
      requestAnimationFrame(frame);
      if (document.hidden) return;
      const delta = Math.min(.05, (now - last) / 1000); last = now;
      context.clearRect(0, 0, width, height);
      for (const dot of dots) {
        dot.x = (dot.x + dot.vx * delta + 1) % 1; dot.y = (dot.y + dot.vy * delta + 1) % 1;
      }
      context.strokeStyle = color; context.fillStyle = color;
      for (let a = 0; a < dots.length; a++) {
        const ax = dots[a].x * width, ay = dots[a].y * height;
        for (let b = a + 1; b < dots.length; b++) {
          const distance = Math.hypot(ax - dots[b].x * width, ay - dots[b].y * height);
          if (distance < 150) { context.globalAlpha = (1 - distance / 150) * .16; context.beginPath(); context.moveTo(ax, ay); context.lineTo(dots[b].x * width, dots[b].y * height); context.stroke(); }
        }
        context.globalAlpha = .45; context.beginPath(); context.arc(ax, ay, dots[a].r, 0, Math.PI * 2); context.fill();
      }
      context.globalAlpha = 1;
    };
    requestAnimationFrame(frame);
  }

  // ---- Data helpers -------------------------------------------------------------------------------------
  // Large tables live outside the injected data ("$files" in the data set) and are fetched on demand.
  const fileCache = new Map();
  const timings = [];

  function parseDelimited(text, delimiter) {
    const rows = [];
    let field = '', row = [], quoted = false;
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      if (quoted) {
        if (c === '"') { if (text[i + 1] === '"') { field += '"'; i++; } else quoted = false; }
        else field += c;
      } else if (c === '"') quoted = true;
      else if (c === delimiter) { row.push(field); field = ''; }
      else if (c === '\n' || c === '\r') {
        if (c === '\r' && text[i + 1] === '\n') i++;
        row.push(field); field = '';
        if (row.length > 1 || row[0] !== '') rows.push(row);
        row = [];
      } else field += c;
    }
    if (field !== '' || row.length) { row.push(field); rows.push(row); }
    const header = rows.shift() || [];
    const numeric = /^-?\d+(\.\d+)?([eE][-+]?\d+)?$/;
    return rows.map(values => {
      const record = {};
      header.forEach((key, index) => {
        const value = values[index] ?? '';
        record[key] = numeric.test(value) ? Number(value) : value;
      });
      return record;
    });
  }

  /** Loads a "$files" entry (or a package-relative path). CSV/TSV become arrays of objects; JSON is parsed. Cached per page. */
  function load(name) {
    const path = (context.files && context.files[name]) || name;
    if (fileCache.has(path)) return fileCache.get(path);
    const started = performance.now();
    const promise = fetch(path, { cache: 'no-store' }).then(response => {
      if (!response.ok) throw new Error(`${path}: HTTP ${response.status}`);
      return response.text();
    }).then(text => {
      const fetched = performance.now();
      const lower = path.toLowerCase();
      const value = lower.endsWith('.json') ? JSON.parse(text)
        : lower.endsWith('.csv') ? parseDelimited(text, ',')
        : lower.endsWith('.tsv') ? parseDelimited(text, '\t')
        : text;
      timings.push({ name, path, bytes: text.length, fetchMs: fetched - started, parseMs: performance.now() - fetched, rows: Array.isArray(value) ? value.length : undefined });
      return value;
    });
    fileCache.set(path, promise);
    promise.catch(() => fileCache.delete(path));
    return promise;
  }

  /** Number/money/date formatting in the data set's locale and currency (data.locale / data.currency). */
  const format = (() => {
    const locale = () => data('locale', undefined) || undefined;
    const currency = () => data('currency', 'USD') || 'USD';
    const cache = new Map();
    const formatter = (kind, options) => {
      const key = kind + JSON.stringify(options) + locale();
      if (!cache.has(key)) cache.set(key, kind === 'date' ? new Intl.DateTimeFormat(locale(), options) : new Intl.NumberFormat(locale(), options));
      return cache.get(key);
    };
    const blank = value => value === null || value === undefined || value === '' || (typeof value === 'number' && !Number.isFinite(value));
    return {
      number: (value, digits = 0) => blank(value) ? '—' : formatter('n', { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value),
      money: (value, options = {}) => blank(value) ? '—' : formatter('n', { style: 'currency', currency: options.currency || currency(), notation: options.compact ? 'compact' : 'standard', maximumFractionDigits: options.digits ?? (options.compact ? 1 : 2), minimumFractionDigits: options.compact ? 0 : (options.digits ?? 2) }).format(value),
      compact: (value, digits = 1) => blank(value) ? '—' : formatter('n', { notation: 'compact', maximumFractionDigits: digits }).format(value),
      percent: (value, digits = 1) => blank(value) ? '—' : formatter('n', { style: 'percent', minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value),
      date: (value, style = 'medium') => {
        if (blank(value)) return '—';
        const date = value instanceof Date ? value : new Date(value);
        return Number.isNaN(date.getTime()) ? String(value) : formatter('date', { dateStyle: style }).format(date);
      },
      dateTime: value => {
        if (blank(value)) return '—';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value) : formatter('date', { dateStyle: 'short', timeStyle: 'short' }).format(date);
      },
      get locale() { return locale() || navigator.language; },
      get currency() { return currency(); }
    };
  })();

  /**
   * Renders rows into a sortable, searchable, paginated table inside `host`.
   * columns: [{ key, label, format: 'money'|'number'|'percent'|'date'|'dateTime'|fn(value,row), digits, align: 'right', className: fn(value,row), sortValue: fn(row) }]
   * options: { pageSize, search: true, placeholder, onRow(row), empty }
   */
  function table(host, rows, columns, options = {}) {
    const state = { rows: Array.isArray(rows) ? rows : [], sort: null, dir: 1, query: '', page: 0, filter: null };
    const pageSize = options.pageSize || 12;
    host.classList.add('fis-table-host');
    host.innerHTML = '';
    const tools = document.createElement('div'); tools.className = 'fis-table-tools';
    const search = document.createElement('input'); search.type = 'search'; search.placeholder = options.placeholder || 'Search…';
    const info = document.createElement('span'); info.className = 'fis-table-info';
    if (options.search !== false) tools.append(search);
    tools.append(info);
    const scroller = document.createElement('div'); scroller.className = 'fis-table-scroll';
    const element = document.createElement('table'); element.className = 'fis-table';
    const head = element.createTHead().insertRow();
    const tbody = element.createTBody();
    scroller.append(element);
    const pager = document.createElement('div'); pager.className = 'fis-table-pager';
    const prev = document.createElement('button'); prev.textContent = '‹';
    const next = document.createElement('button'); next.textContent = '›';
    const pageLabel = document.createElement('span');
    pager.append(prev, pageLabel, next);
    host.append(tools, scroller, pager);

    const formatValue = (column, value, row) => {
      if (typeof column.format === 'function') return column.format(value, row);
      if (column.format && format[column.format]) return format[column.format](value, column.digits);
      return value === null || value === undefined || value === '' ? '—' : String(value);
    };
    columns.forEach(column => {
      const th = document.createElement('th');
      th.textContent = column.label ?? column.key;
      if (column.align) th.style.textAlign = column.align;
      th.addEventListener('click', () => {
        state.dir = state.sort === column ? -state.dir : 1;
        state.sort = column; state.page = 0; render();
      });
      head.append(th);
    });
    let searchTimer;
    search.addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(() => { state.query = search.value.trim().toLowerCase(); state.page = 0; render(); }, 120); });
    prev.addEventListener('click', () => { state.page--; render(); });
    next.addEventListener('click', () => { state.page++; render(); });

    function visible() {
      let list = state.filter ? state.rows.filter(state.filter) : state.rows;
      if (state.query) list = list.filter(row => columns.some(column => String(row[column.key] ?? '').toLowerCase().includes(state.query)));
      if (state.sort) {
        const column = state.sort, dir = state.dir;
        const read = column.sortValue || (row => row[column.key]);
        list = [...list].sort((a, b) => {
          const x = read(a), y = read(b);
          if (x == null) return 1; if (y == null) return -1;
          return (typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y))) * dir;
        });
      }
      return list;
    }

    function render() {
      const started = performance.now();
      const list = visible();
      const pages = Math.max(1, Math.ceil(list.length / pageSize));
      state.page = Math.min(Math.max(0, state.page), pages - 1);
      const slice = list.slice(state.page * pageSize, state.page * pageSize + pageSize);
      [...head.children].forEach((th, index) => th.dataset.sort = columns[index] === state.sort ? (state.dir > 0 ? 'asc' : 'desc') : '');
      const fragment = document.createDocumentFragment();
      slice.forEach(row => {
        const tr = document.createElement('tr');
        columns.forEach(column => {
          const td = document.createElement('td');
          const value = row[column.key];
          td.textContent = formatValue(column, value, row);
          if (column.align) td.style.textAlign = column.align;
          const extra = typeof column.className === 'function' ? column.className(value, row) : column.className;
          if (extra) td.className = extra;
          tr.append(td);
        });
        if (options.onRow) { tr.classList.add('clickable'); tr.addEventListener('click', () => { tbody.querySelectorAll('tr.selected').forEach(other => other.classList.remove('selected')); tr.classList.add('selected'); options.onRow(row); }); }
        fragment.append(tr);
      });
      tbody.replaceChildren(fragment);
      if (!slice.length) {
        const tr = tbody.insertRow(); const td = tr.insertCell();
        td.colSpan = columns.length; td.className = 'fis-table-empty'; td.textContent = options.empty || 'No rows in this data set.';
      }
      info.textContent = `${format.number(list.length)} of ${format.number(state.rows.length)} rows`;
      pageLabel.textContent = `${state.page + 1} / ${pages}`;
      prev.disabled = state.page === 0; next.disabled = state.page >= pages - 1;
      api.lastRenderMs = performance.now() - started;
      return api;
    }
    const api = {
      render,
      setRows: list => { state.rows = Array.isArray(list) ? list : []; state.page = 0; return render(); },
      setFilter: predicate => { state.filter = predicate; state.page = 0; return render(); },
      visible,
      lastRenderMs: 0
    };
    return render();
  }

  window.FISDeck = {
    demo,
    context,
    data,
    rows: (path) => { const value = data(path, []); return Array.isArray(value) ? value : []; },
    load,
    parseCsv: text => parseDelimited(text, ','),
    timings,
    format,
    table,
    count: (element, value) => animateCount(element, value),
    onTab: listener => tabListeners.push(listener)
  };
  personalise();
  chrome();
  backdrop();
  tabs();
  reveal();
  counters();
})();
