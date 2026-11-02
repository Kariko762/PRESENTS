// Data Lab page helpers. Loaded with `defer` after shared/deck.js, so FISDeck exists when this runs.
window.Lab = (() => {
  'use strict';
  const deck = window.FISDeck;
  const context = deck.context;
  const svgNs = 'http://www.w3.org/2000/svg';

  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

  function el(tag, attributes = {}, ...children) {
    const element = document.createElement(tag);
    for (const [key, value] of Object.entries(attributes)) {
      if (key === 'text') element.textContent = value;
      else if (key === 'class') element.className = value;
      else if (key.startsWith('on')) element.addEventListener(key.slice(2), value);
      else element.setAttribute(key, value);
    }
    children.flat().forEach(child => child != null && element.append(child));
    return element;
  }

  function svg(tag, attributes = {}) {
    const element = document.createElementNS(svgNs, tag);
    for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value);
    return element;
  }

  const kb = bytes => bytes >= 1048576 ? `${(bytes / 1048576).toFixed(2)} MB` : `${(bytes / 1024).toFixed(1)} KB`;

  /** How many table rows fit in `host` (search bar, header and pager excluded), so tables never scroll inside a page. */
  const fitRows = (host, rowHeight = 31, reserve = 124) => Math.max(5, Math.floor((host.getBoundingClientRect().height - reserve) / rowHeight));

  /** Shows the data-set chip and, when the Player reported a problem, a banner explaining the fallback. */
  function status(target = document.querySelector('[data-lab-status]')) {
    if (!target) return;
    const chip = el('span', { class: 'lab-chip' }, 'Data set ', el('b', { text: context.dataSetName || 'none' }),
      context.dataBytes ? ` · ${kb(context.dataBytes)} inline` : '');
    target.append(chip);
    const stage = document.querySelector('.stage');
    if (context.dataError) {
      stage.insertBefore(el('div', { class: 'lab-banner error reveal in', text: `Data set problem — pages are showing built-in defaults. ${context.dataError}` }), stage.children[1]);
    } else if (!context.data) {
      stage.insertBefore(el('div', { class: 'lab-banner warn reveal in', text: 'No data set selected — pages are showing built-in defaults.' }), stage.children[1]);
    }
  }

  /** Line chart: xs = labels, series = [{ values, color, label, area }]. Returns { highlight(index) }. */
  function lineChart(host, xs, series, options = {}) {
    host.innerHTML = '';
    const width = host.clientWidth || 800, height = host.clientHeight || 300, pad = { l: 64, r: 16, t: 12, b: 26 };
    const root = svg('svg', { class: 'chart', viewBox: `0 0 ${width} ${height}`, preserveAspectRatio: 'none' });
    host.append(root);
    if (!xs.length) {
      const text = svg('text', { x: width / 2, y: height / 2, 'text-anchor': 'middle' });
      text.textContent = 'No points in this data set';
      root.append(text);
      return { highlight() {} };
    }
    const all = series.flatMap(s => s.values).filter(Number.isFinite);
    let min = Math.min(...all), max = Math.max(...all);
    if (min === max) { min -= 1; max += 1; }
    const x = index => pad.l + (xs.length === 1 ? 0 : index / (xs.length - 1)) * (width - pad.l - pad.r);
    const y = value => pad.t + (1 - (value - min) / (max - min)) * (height - pad.t - pad.b);
    for (let tick = 0; tick <= 4; tick++) {
      const value = min + (max - min) * tick / 4;
      root.append(svg('line', { class: 'grid', x1: pad.l, x2: width - pad.r, y1: y(value), y2: y(value) }));
      const label = svg('text', { x: pad.l - 8, y: y(value) + 4, 'text-anchor': 'end' });
      label.textContent = (options.format || deck.format.compact)(value);
      root.append(label);
    }
    const step = Math.max(1, Math.ceil(xs.length / 6));
    for (let index = 0; index < xs.length; index += step) {
      const label = svg('text', { x: x(index), y: height - 6, 'text-anchor': 'middle' });
      label.textContent = options.xFormat ? options.xFormat(xs[index]) : xs[index];
      root.append(label);
    }
    if (min < 0 && max > 0) root.append(svg('line', { x1: pad.l, x2: width - pad.r, y1: y(0), y2: y(0), stroke: '#53687a', 'stroke-dasharray': '4 4' }));
    series.forEach(s => {
      const points = s.values.map((value, index) => `${x(index).toFixed(1)},${y(value).toFixed(1)}`);
      if (s.area) root.append(svg('path', { d: `M${x(0)},${y(Math.max(min, 0))} L${points.join(' L')} L${x(s.values.length - 1)},${y(Math.max(min, 0))} Z`, fill: s.color, opacity: .12 }));
      root.append(svg('polyline', { points: points.join(' '), fill: 'none', stroke: s.color, 'stroke-width': 2, 'vector-effect': 'non-scaling-stroke' }));
    });
    const cursor = svg('line', { y1: pad.t, y2: height - pad.b, stroke: '#a9b8c2', 'stroke-dasharray': '3 3', opacity: 0 });
    root.append(cursor);
    const tip = document.querySelector('.tooltip') || document.body.appendChild(el('div', { class: 'tooltip' }));
    root.addEventListener('mousemove', event => {
      const box = root.getBoundingClientRect();
      const ratio = ((event.clientX - box.left) / box.width * width - pad.l) / (width - pad.l - pad.r);
      const index = Math.round(Math.min(1, Math.max(0, ratio)) * (xs.length - 1));
      cursor.setAttribute('x1', x(index)); cursor.setAttribute('x2', x(index)); cursor.setAttribute('opacity', 1);
      tip.innerHTML = `<b>${esc(options.xFormat ? options.xFormat(xs[index]) : xs[index])}</b><br>` +
        series.map(s => `<span style="color:${s.color}">●</span> ${esc(s.label)}: ${esc((options.format || deck.format.number)(s.values[index]))}`).join('<br>');
      tip.style.display = 'block'; tip.style.left = `${event.clientX + 14}px`; tip.style.top = `${event.clientY + 14}px`;
    });
    root.addEventListener('mouseleave', () => { cursor.setAttribute('opacity', 0); tip.style.display = 'none'; });
    return root;
  }

  /** Donut chart from [{ label, value, color }]. */
  function donut(host, items, centre) {
    host.innerHTML = '';
    const size = 220, radius = 84, stroke = 30, total = items.reduce((sum, item) => sum + Math.abs(item.value), 0);
    const root = svg('svg', { viewBox: `0 0 ${size} ${size}`, width: '100%', height: '100%' });
    root.append(svg('circle', { cx: size / 2, cy: size / 2, r: radius, fill: 'none', stroke: '#1d2c3b', 'stroke-width': stroke }));
    let offset = 0;
    const circumference = 2 * Math.PI * radius;
    items.forEach(item => {
      const share = total ? Math.abs(item.value) / total : 0;
      const arc = svg('circle', { cx: size / 2, cy: size / 2, r: radius, fill: 'none', stroke: item.color, 'stroke-width': stroke,
        'stroke-dasharray': `${share * circumference} ${circumference}`, 'stroke-dashoffset': -offset * circumference, transform: `rotate(-90 ${size / 2} ${size / 2})` });
      const title = svg('title'); title.textContent = `${item.label}: ${(share * 100).toFixed(1)}%`; arc.append(title);
      root.append(arc);
      offset += share;
    });
    const label = svg('text', { x: size / 2, y: size / 2 - 2, 'text-anchor': 'middle', fill: '#f3f6f8', 'font-size': 20, 'font-weight': 700 });
    label.textContent = centre?.value ?? '';
    const sub = svg('text', { x: size / 2, y: size / 2 + 18, 'text-anchor': 'middle', fill: '#a9b8c2', 'font-size': 11 });
    sub.textContent = centre?.label ?? '';
    root.append(label, sub);
    host.append(root);
  }

  /** Vertical bars: [{ label, value }]. */
  function bars(host, items, options = {}) {
    host.innerHTML = '';
    const width = host.clientWidth || 600, height = host.clientHeight || 220, pad = { l: 56, r: 8, t: 10, b: 24 };
    const root = svg('svg', { class: 'chart', viewBox: `0 0 ${width} ${height}`, preserveAspectRatio: 'none' });
    host.append(root);
    const max = Math.max(1, ...items.map(item => item.value));
    const slot = (width - pad.l - pad.r) / Math.max(1, items.length);
    for (let tick = 0; tick <= 3; tick++) {
      const value = max * tick / 3, yy = pad.t + (1 - tick / 3) * (height - pad.t - pad.b);
      root.append(svg('line', { class: 'grid', x1: pad.l, x2: width - pad.r, y1: yy, y2: yy }));
      const text = svg('text', { x: pad.l - 6, y: yy + 4, 'text-anchor': 'end' }); text.textContent = (options.format || deck.format.compact)(value); root.append(text);
    }
    const every = Math.max(1, Math.ceil(46 / slot));
    items.forEach((item, index) => {
      const h = (item.value / max) * (height - pad.t - pad.b);
      const rect = svg('rect', { x: pad.l + index * slot + slot * .15, y: height - pad.b - h, width: slot * .7, height: Math.max(0, h), rx: 2, fill: options.color || 'var(--accent)', opacity: .85 });
      const title = svg('title'); title.textContent = `${item.label}: ${(options.format || deck.format.number)(item.value)}`; rect.append(title);
      root.append(rect);
      if (index % every === 0) {
        const text = svg('text', { x: pad.l + index * slot + slot / 2, y: height - 6, 'text-anchor': 'middle' }); text.textContent = item.label; root.append(text);
      }
    });
  }

  const palette = ['#4bcd3e', '#43cebd', '#c6e65b', '#3bcff0', '#a0f0af', '#ffc45c', '#ff8b7d', '#9c8cff'];

  return { deck, context, esc, el, svg, kb, fitRows, status, lineChart, donut, bars, palette };
})();
