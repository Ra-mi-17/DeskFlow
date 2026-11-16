'use strict';
/* global deskflow, icon, formatTime, makeToaster */

const $ = (id) => document.getElementById(id);
const body = document.body;
const list = $('list');
const volEl = $('vol');
const toast = makeToaster($('toast'));
const send = (action, value) => deskflow.send('cmd', { action, value });

let S = null;
let listSig = '';
let editing = false;
let dragId = null;

const TYPE_LABEL = { file: 'ファイル', youtube: 'YouTube', vimeo: 'Vimeo', url: 'URL', web: 'Web' };

// ------------------------------------------------------------------ static UI
$('add-file').innerHTML = `${icon('file')}<span>ファイル</span>`;
$('add-folder').innerHTML = `${icon('folder')}<span>フォルダ</span>`;
$('more').innerHTML = icon('more');
document.querySelector('.url-ic').innerHTML = icon('link');
$('prev').innerHTML = icon('prev');
$('next').innerHTML = icon('next');
$('shuffle').innerHTML = icon('shuffle');

$('add-file').onclick = () => send('pickFiles');
$('add-folder').onclick = () => send('pickFolder');
$('prev').onclick = () => send('prev');
$('next').onclick = () => send('next');
$('play').onclick = () => send('togglePause');
$('shuffle').onclick = () => send('set', { key: 'shuffle', value: !S.shuffle });
$('repeat').onclick = () => send('set', { key: 'repeat', value: S.repeat === 'all' ? 'one' : 'all' });
$('mute').onclick = () => send('set', { key: 'muted', value: !S.muted });

$('more').onclick = (e) => { e.stopPropagation(); $('more-menu').hidden = !$('more-menu').hidden; };
document.addEventListener('click', () => { $('more-menu').hidden = true; });
$('more-menu').addEventListener('click', (e) => {
  const act = e.target.closest('button')?.dataset.act;
  if (act) send(act);
});

$('url-form').addEventListener('submit', (e) => {
  e.preventDefault();
  const text = $('url').value.trim();
  if (!text) return;
  send('addUrls', { text, playNow: false });
  $('url').value = '';
});

let volDragging = false;
volEl.addEventListener('pointerdown', () => { volDragging = true; });
window.addEventListener('pointerup', () => { volDragging = false; });
volEl.addEventListener('input', () => {
  volEl.style.setProperty('--p', `${volEl.value}%`);
  $('vol-val').textContent = volEl.value;
  send('set', { key: 'volume', value: Number(volEl.value) });
});

// ------------------------------------------------------------------ state
deskflow.on('state', (st) => { S = st; render(); });
deskflow.on('toast', (msg) => toast(msg));
deskflow.send('ready');

function render() {
  const sig = JSON.stringify([S.currentId, S.playlist.map((i) => [i.id, i.title, i.enabled, i.thumb, i.author])]);
  if (sig !== listSig && !editing) { listSig = sig; renderList(); }
  renderFooter();
}

function renderFooter() {
  const cur = S.playlist.find((i) => i.id === S.currentId);
  body.classList.toggle('paused', !!S.effectivePaused);
  $('now-title').textContent = cur ? cur.title : '—';
  const a = S.autoPaused || {};
  $('now-label').textContent = S.paused ? '一時停止中'
    : a.fullscreen ? '一時停止中（全画面アプリ使用中）'
      : a.battery ? '一時停止中（バッテリー駆動）'
        : a.lock ? '一時停止中（画面ロック）' : (cur ? '再生中' : '');
  $('play').innerHTML = icon(S.effectivePaused ? 'play' : 'pause');
  $('shuffle').classList.toggle('on', !!S.shuffle);
  $('repeat').classList.add('on');
  $('repeat').innerHTML = icon(S.repeat === 'one' ? 'repeatOne' : 'repeat');
  $('repeat').title = S.repeat === 'one' ? '1本をくり返す（クリックで切替）' : 'リスト全体をループ（クリックで切替）';
  $('mute').innerHTML = icon(S.muted || S.volume === 0 ? 'mute' : 'volume');
  if (!volDragging) {
    volEl.value = String(S.volume);
    volEl.style.setProperty('--p', `${S.volume}%`);
    $('vol-val').textContent = String(S.volume);
  }
  const en = S.playlist.filter((i) => i.enabled !== false).length;
  $('count').textContent = S.playlist.length ? `${S.playlist.length} 本${en !== S.playlist.length ? `（ループ対象 ${en}）` : ''}` : '';
}

function el(tag, cls, html) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (html != null) e.innerHTML = html;
  return e;
}

function describe(it) {
  if (it.type === 'file') return it.src;
  if (it.type === 'youtube') return it.author || `youtu.be/${it.src}`;
  if (it.type === 'vimeo') return it.author || `vimeo.com/${it.src}`;
  return it.src;
}

function renderList() {
  body.classList.toggle('is-empty', S.playlist.length === 0);
  list.textContent = '';
  S.playlist.forEach((it, index) => {
    const li = el('li', 'item');
    li.dataset.id = it.id;
    li.dataset.index = String(index);
    li.draggable = true;
    if (it.id === S.currentId) li.classList.add('current');
    if (it.enabled === false) li.classList.add('disabled');

    li.append(el('span', 'handle', icon('grip')));

    const cb = el('input', 'en');
    cb.type = 'checkbox';
    cb.checked = it.enabled !== false;
    cb.title = 'ループ再生に含める';
    cb.onchange = () => send('setEnabled', { id: it.id, enabled: cb.checked });
    li.append(cb);

    const th = el('div', 'thumb');
    th.title = 'この動画を再生';
    th.innerHTML = icon(it.type === 'youtube' ? 'youtube' : it.type === 'file' ? 'file' : 'globe');
    const src = thumbFor(it);
    if (src) {
      const img = new Image();
      img.src = src;
      img.onerror = () => img.remove();
      th.append(img);
    }
    const d = durations.get(it.id);
    if (d) th.append(el('span', 'dur', formatTime(d)));
    if (it.id === S.currentId) th.append(el('span', 'eq', '<i></i><i></i><i></i>'));
    th.append(el('div', 'play-ov', icon('play')));
    th.onclick = () => send('play', index);
    li.append(th);

    const meta = el('div', 'meta');
    const t = el('div', 't');
    t.textContent = it.title;
    t.title = it.title;
    t.ondblclick = (e) => { e.stopPropagation(); startRename(t, it); };
    const sub = el('div', 'sub');
    sub.append(el('span', `tag ${it.type}`, TYPE_LABEL[it.type] || it.type));
    const s = el('span', 'src');
    s.textContent = describe(it);
    s.title = it.src;
    sub.append(s);
    meta.append(t, sub);
    li.append(meta);

    const acts = el('div', 'acts');
    const mk = (name, title, fn, cls = '') => {
      const b = el('button', cls, icon(name));
      b.title = title;
      b.onclick = (e) => { e.stopPropagation(); fn(); };
      acts.append(b);
    };
    mk('edit', '名前を変更', () => startRename(t, it));
    mk('up', '上へ', () => send('move', { from: index, to: index - 1 }));
    mk('down', '下へ', () => send('move', { from: index, to: index + 1 }));
    mk('trash', '削除', () => send('remove', it.id), 'del');
    li.append(acts);

    li.ondblclick = () => send('play', index);
    li.addEventListener('dragstart', (e) => {
      dragId = it.id;
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('application/x-deskflow', it.id);
      li.classList.add('dragging');
    });
    li.addEventListener('dragend', () => { dragId = null; li.classList.remove('dragging'); clearMarks(); });

    list.append(li);
  });
  queueThumbs();
}

function startRename(t, it) {
  if (editing) return;
  editing = true;
  t.contentEditable = 'true';
  t.focus();
  const range = document.createRange();
  range.selectNodeContents(t);
  const sel = window.getSelection();
  sel.removeAllRanges();
  sel.addRange(range);
  const finish = (commit) => {
    t.removeEventListener('keydown', onKey);
    t.removeEventListener('blur', onBlur);
    t.contentEditable = 'false';
    editing = false;
    const v = t.textContent.trim();
    if (commit && v && v !== it.title) send('rename', { id: it.id, title: v });
    else { t.textContent = it.title; }
    listSig = '';
    render();
  };
  const onKey = (e) => {
    if (e.key === 'Enter') { e.preventDefault(); finish(true); }
    if (e.key === 'Escape') { e.preventDefault(); finish(false); }
  };
  const onBlur = () => finish(true);
  t.addEventListener('keydown', onKey);
  t.addEventListener('blur', onBlur);
}

// ------------------------------------------------------------------ reorder (drag & drop)
function clearMarks() {
  list.querySelectorAll('.drop-before, .drop-after').forEach((n) => n.classList.remove('drop-before', 'drop-after'));
}

list.addEventListener('dragover', (e) => {
  if (!dragId) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = 'move';
  const li = e.target.closest('li.item');
  clearMarks();
  if (!li || li.dataset.id === dragId) return;
  const r = li.getBoundingClientRect();
  li.classList.add(e.clientY < r.top + r.height / 2 ? 'drop-before' : 'drop-after');
});

list.addEventListener('drop', (e) => {
  if (!dragId) return;
  e.preventDefault();
  e.stopPropagation();
  const li = e.target.closest('li.item');
  const from = S.playlist.findIndex((i) => i.id === dragId);
  clearMarks();
  if (!li || from < 0) return;
  const target = Number(li.dataset.index);
  const r = li.getBoundingClientRect();
  let to = target + (e.clientY < r.top + r.height / 2 ? 0 : 1);
  if (from < to) to -= 1;
  if (to !== from) send('move', { from, to });
  dragId = null;
});

// ------------------------------------------------------------------ external drops (files / URLs)
let depth = 0;
const isExternal = (e) => !dragId && [...e.dataTransfer.types].some((t) => t === 'Files' || t === 'text/uri-list' || t === 'text/plain');
document.addEventListener('dragenter', (e) => { if (isExternal(e)) { depth += 1; body.classList.add('dropping'); } });
document.addEventListener('dragleave', (e) => { if (isExternal(e)) { depth = Math.max(0, depth - 1); if (!depth) body.classList.remove('dropping'); } });
document.addEventListener('dragover', (e) => { if (isExternal(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
document.addEventListener('drop', (e) => {
  if (dragId) return;
  e.preventDefault();
  depth = 0;
  body.classList.remove('dropping');
  if (e.dataTransfer.files.length) { deskflow.sendFiles(e.dataTransfer.files, { playNow: false }); return; }
  const text = e.dataTransfer.getData('text/uri-list') || e.dataTransfer.getData('text/plain');
  if (text) send('addUrls', { text, playNow: false });
});

document.addEventListener('paste', (e) => {
  if (e.target === $('url') || editing) return;
  const text = e.clipboardData.getData('text');
  if (text) send('addUrls', { text, playNow: false });
});

document.addEventListener('keydown', (e) => {
  if (e.target.tagName === 'INPUT' || editing) return;
  if (e.key === ' ') { e.preventDefault(); send('togglePause'); }
});

// ------------------------------------------------------------------ thumbnails for local files
const thumbs = new Map();     // id -> dataURL | null
const durations = new Map();  // id -> seconds
let thumbBusy = false;

function thumbFor(it) {
  if (it.thumb) return it.thumb;
  if (it.type === 'youtube') return `https://i.ytimg.com/vi/${it.src}/mqdefault.jpg`;
  return thumbs.get(it.id) || null;
}

function queueThumbs() {
  if (thumbBusy) return;
  const it = S.playlist.find((i) => i.type === 'file' && !thumbs.has(i.id));
  if (!it) return;
  thumbBusy = true;
  makeThumb(it).then((url) => {
    thumbs.set(it.id, url);
    thumbBusy = false;
    if (url && !editing) { listSig = ''; render(); } else queueThumbs();
  });
}

function makeThumb(it) {
  return new Promise((resolve) => {
    const v = document.createElement('video');
    let done = false;
    const finish = (val) => {
      if (done) return;
      done = true;
      v.removeAttribute('src');
      v.load();
      resolve(val);
    };
    v.muted = true;
    v.preload = 'metadata';
    v.addEventListener('loadedmetadata', () => {
      if (Number.isFinite(v.duration)) durations.set(it.id, v.duration);
      v.currentTime = Math.min(4, (v.duration || 0) * 0.1);
    });
    v.addEventListener('seeked', () => {
      try {
        const c = document.createElement('canvas');
        c.width = 224; c.height = 126;
        const ctx = c.getContext('2d');
        const vr = v.videoWidth / v.videoHeight || 16 / 9;
        const cr = c.width / c.height;
        let sw = v.videoWidth; let sh = v.videoHeight; let sx = 0; let sy = 0;
        if (vr > cr) { sw = sh * cr; sx = (v.videoWidth - sw) / 2; } else { sh = sw / cr; sy = (v.videoHeight - sh) / 2; }
        ctx.drawImage(v, sx, sy, sw, sh, 0, 0, c.width, c.height);
        finish(c.toDataURL('image/jpeg', 0.75));
      } catch { finish(null); }
    });
    v.addEventListener('error', () => finish(null));
    setTimeout(() => finish(null), 8000);
    v.src = it.mediaUrl;
  });
}
