'use strict';
/* global deskflow, icon, formatTime, makeToaster, YT, Vimeo */

const $ = (id) => document.getElementById(id);
const body = document.body;
const video = $('video');
const ytWrap = $('yt-wrap');
const vimeoWrap = $('vimeo-wrap');
const shield = $('shield');
const volEl = $('vol');
const toast = makeToaster($('toast'));

let S = null;            // latest state from main
let loadedToken = null;  // playToken of the item currently loaded
let kind = null;         // 'video' | 'yt' | 'vimeo' | null
let cur = null;          // current item
let failTimer = null;

let yt = null; let ytPromise = null; let ytAspect = 16 / 9; let ytLoopTimer = null;
let vimeo = null; let vimeoAspect = 16 / 9; let vimeoLoop = null; let vimeoTimes = [0, 0];

const send = (action, value) => deskflow.send('cmd', { action, value });
const enabledCount = () => (S ? S.playlist.filter((i) => i.enabled !== false).length : 0);
const singleLoop = () => !!S && (S.repeat === 'one' || enabledCount() <= 1);

// ------------------------------------------------------------------ static UI
$('btn-prev').innerHTML = icon('prev');
$('btn-next').innerHTML = icon('next');
$('btn-list').innerHTML = icon('list');
$('btn-menu').innerHTML = icon('more');

$('btn-prev').onclick = () => send('prev');
$('btn-next').onclick = () => send('next');
$('btn-play').onclick = () => send('togglePause');
$('btn-mute').onclick = () => send('set', { key: 'muted', value: !S.muted });
$('btn-list').onclick = () => send('openPlaylist');
$('btn-menu').onclick = () => deskflow.send('show-menu');

let volDragging = false;
volEl.addEventListener('pointerdown', () => { volDragging = true; });
window.addEventListener('pointerup', () => { volDragging = false; });
volEl.addEventListener('input', () => {
  volEl.style.setProperty('--p', `${volEl.value}%`);
  send('set', { key: 'volume', value: Number(volEl.value) });
});

// ------------------------------------------------------------------ state
deskflow.on('state', (state) => { S = state; render(); });
deskflow.on('toast', (msg) => toast(msg));
deskflow.send('ready');

function currentItem() {
  return S.playlist.find((it) => it.id === S.currentId) || null;
}

function render() {
  body.classList.toggle('wallpaper', S.mode === 'wallpaper');
  body.classList.toggle('locked', !!S.locked);
  body.classList.toggle('controls-on', !!S.showControls);
  body.classList.toggle('paused', !!S.paused);
  body.classList.toggle('autopaused', !S.paused && S.effectivePaused);
  body.dataset.fit = S.fit;
  $('dim').style.opacity = String(1 - (S.brightness ?? 1));

  const a = S.autoPaused || {};
  $('badge').textContent = S.paused ? '一時停止中'
    : a.fullscreen ? '全画面アプリ使用中のため一時停止'
      : a.battery ? 'バッテリー駆動中のため一時停止'
        : a.lock ? '画面ロック中' : '';

  if (S.playToken !== loadedToken) {
    loadedToken = S.playToken;
    load(currentItem());
  } else {
    const it = currentItem();
    if (it && cur && it.id === cur.id) cur = it; // pick up renamed titles etc.
  }
  applyPlayback();
  layoutFrames();
  updateControls();
}

function updateControls() {
  $('btn-play').innerHTML = icon(S.effectivePaused ? 'play' : 'pause');
  $('btn-mute').innerHTML = icon(S.muted || S.volume === 0 ? 'mute' : 'volume');
  if (!volDragging) {
    volEl.value = String(S.volume);
    volEl.style.setProperty('--p', `${S.volume}%`);
  }
  $('title').textContent = cur ? cur.title : '';
  const multi = enabledCount() > 1;
  $('btn-prev').style.opacity = multi ? '' : '.35';
  $('btn-next').style.opacity = multi ? '' : '.35';
}

// ------------------------------------------------------------------ loading
function setLayer(name) {
  video.classList.toggle('off', name !== 'video');
  ytWrap.classList.toggle('off', name !== 'yt');
  vimeoWrap.classList.toggle('off', name !== 'vimeo');
}

function stopAll() {
  clearTimeout(failTimer);
  clearInterval(ytLoopTimer);
  body.classList.remove('loading');
  if (video.getAttribute('src')) {
    video.pause();
    video.removeAttribute('src');
    video.load();
  }
  if (yt && kind === 'yt') { try { yt.stopVideo(); } catch { /* ignore */ } }
  if (vimeo) {
    const v = vimeo; vimeo = null; vimeoLoop = null;
    v.destroy().catch(() => {});
    vimeoWrap.innerHTML = '';
  }
  kind = null;
  setLayer(null);
}

async function load(item) {
  stopAll();
  cur = item;
  $('empty').classList.toggle('off', !!item);
  if (!item) return;
  const token = loadedToken;
  try {
    switch (item.type) {
      case 'file': playVideo(item.mediaUrl); break;
      case 'url': playVideo(item.src); break;
      case 'web': {
        body.classList.add('loading');
        toast('動画を取得しています…');
        const r = await deskflow.invoke('resolve', item.id);
        if (token !== loadedToken) return;
        body.classList.remove('loading');
        if (!r || r.error) throw new Error((r && r.error) || '再生できません');
        playVideo(r.url);
        break;
      }
      case 'youtube': await playYouTube(item, token); break;
      case 'vimeo': await playVimeo(item, token); break;
      default: throw new Error(`未対応の種類です: ${item.type}`);
    }
  } catch (e) {
    if (token === loadedToken) fail(e.message || String(e));
  }
}

/** Show an error and skip to the next item (when there is one). */
function fail(msg) {
  body.classList.remove('loading');
  toast(msg, 5000);
  const id = cur && cur.id;
  const token = loadedToken;
  clearTimeout(failTimer);
  if (enabledCount() > 1) {
    failTimer = setTimeout(() => { if (token === loadedToken && id) deskflow.send('ended', id); }, 3000);
  }
}

// ------------------------------------------------------------------ <video>
function playVideo(src) {
  kind = 'video';
  setLayer('video');
  video.loop = singleLoop();
  video.src = src;
  applyPlayback();
}

video.addEventListener('ended', () => {
  if (kind === 'video' && !video.loop && cur) deskflow.send('ended', cur.id);
});
video.addEventListener('loadedmetadata', () => {
  if (video.videoWidth && video.videoHeight) deskflow.send('aspect', video.videoWidth / video.videoHeight);
});
video.addEventListener('waiting', () => body.classList.add('loading'));
video.addEventListener('playing', () => body.classList.remove('loading'));
video.addEventListener('error', () => {
  if (kind !== 'video' || !video.getAttribute('src')) return;
  const code = video.error && video.error.code;
  const msg = code === 4 ? 'この動画形式には対応していません（MP4 / WebM 推奨）'
    : code === 2 ? 'ネットワークエラーで再生できませんでした'
      : code === 3 ? '動画のデコードに失敗しました' : '動画を再生できませんでした';
  fail(msg);
});

// ------------------------------------------------------------------ YouTube
function loadScript(src) {
  return new Promise((resolve, reject) => {
    const el = document.createElement('script');
    el.src = src;
    el.onload = resolve;
    el.onerror = () => reject(new Error(`読み込み失敗: ${src}`));
    document.head.appendChild(el);
  });
}

function ensureYT() {
  if (ytPromise) return ytPromise;
  ytPromise = new Promise((resolve, reject) => {
    const timer = setTimeout(() => { ytPromise = null; reject(new Error('YouTube プレーヤーを読み込めませんでした（ネット接続を確認してください）')); }, 20000);
    window.onYouTubeIframeAPIReady = () => {
      yt = new YT.Player('yt', {
        width: '100%',
        height: '100%',
        playerVars: {
          autoplay: 1, controls: 0, disablekb: 1, fs: 0, iv_load_policy: 3,
          modestbranding: 1, playsinline: 1, rel: 0, cc_load_policy: 0, origin: location.origin,
          hl: 'ja',
        },
        events: {
          onReady: () => { clearTimeout(timer); resolve(yt); },
          onStateChange: onYtState,
          onApiChange: () => applyCaptions(true),
          onError: onYtError,
        },
      });
    };
    loadScript('https://www.youtube.com/iframe_api').catch(() => {
      clearTimeout(timer);
      ytPromise = null;
      reject(new Error('YouTube API を読み込めませんでした（ネット接続を確認してください）'));
    });
  });
  return ytPromise;
}

async function playYouTube(item, token) {
  body.classList.add('loading');
  const p = await ensureYT();
  if (token !== loadedToken) return;
  kind = 'yt';
  setLayer('yt');
  ytAspect = item.aspect || 16 / 9;
  deskflow.send('aspect', ytAspect);
  p.loadVideoById({ videoId: item.src, startSeconds: item.start || 0 });
  applyPlayback();
  layoutFrames();
  // Seamless loop: jump back just before the end so YouTube's end screen never shows.
  ytLoopTimer = setInterval(() => {
    if (kind !== 'yt' || !yt || !singleLoop()) return;
    const d = yt.getDuration(); const t = yt.getCurrentTime();
    if (d > 2 && t >= d - 0.4 && yt.getPlayerState() === 1) yt.seekTo(cur.start || 0, true);
  }, 200);
}

function onYtState(e) {
  if (kind !== 'yt') return;
  if (e.data === 0) { // ENDED
    if (singleLoop()) { yt.seekTo((cur && cur.start) || 0, true); yt.playVideo(); } else if (cur) deskflow.send('ended', cur.id);
  } else if (e.data === 1) { // PLAYING
    body.classList.remove('loading');
    applyCaptions(true);
    if (S.effectivePaused) yt.pauseVideo();
  } else if (e.data === 3) {
    body.classList.add('loading');
  } else {
    body.classList.remove('loading');
  }
}

async function onYtError(e) {
  if (kind !== 'yt' || !cur) return;
  const code = e.data;
  const item = cur; const token = loadedToken;
  if (code === 101 || code === 150 || code === 153) {
    // Embedding disabled: try a direct stream through yt-dlp.
    toast('埋め込みが許可されていない動画です。yt-dlp での再生を試みます…', 4000);
    const r = await deskflow.invoke('resolve', item.id);
    if (token !== loadedToken) return;
    if (r && r.url) { stopAll(); cur = item; playVideo(r.url); return; }
    fail((r && r.error) || '埋め込み再生できない動画です');
    return;
  }
  fail(code === 100 ? '動画が見つかりません（削除または非公開）' : `YouTube エラー（コード ${code}）`);
}

// ------------------------------------------------------------------ Vimeo
async function playVimeo(item, token) {
  body.classList.add('loading');
  if (!window.Vimeo) await loadScript('https://player.vimeo.com/api/player.js');
  if (token !== loadedToken) return;
  kind = 'vimeo';
  setLayer('vimeo');
  vimeoAspect = item.aspect || 16 / 9;
  const holder = document.createElement('div');
  vimeoWrap.appendChild(holder);
  const opts = {
    autoplay: true, controls: false, loop: singleLoop(), muted: !!S.muted,
    title: false, byline: false, portrait: false, dnt: true, keyboard: false,
  };
  if (item.hash) opts.url = `https://vimeo.com/${item.src}/${item.hash}`; else opts.id = Number(item.src);
  const v = new Vimeo.Player(holder, opts);
  vimeo = v;
  vimeoLoop = opts.loop;
  v.on('ended', () => { if (vimeo === v && !singleLoop() && cur) deskflow.send('ended', cur.id); });
  v.on('error', (err) => { if (vimeo === v) fail(`Vimeo: ${err.message || err.name}`); });
  v.on('play', () => body.classList.remove('loading'));
  v.on('timeupdate', (d) => { vimeoTimes = [d.seconds, d.duration]; });
  v.ready().then(async () => {
    if (vimeo !== v) return;
    try {
      const [w, h] = await Promise.all([v.getVideoWidth(), v.getVideoHeight()]);
      if (w && h) { vimeoAspect = w / h; deskflow.send('aspect', vimeoAspect); }
    } catch { /* ignore */ }
    layoutFrames();
    applyPlayback();
  }).catch((err) => fail(`Vimeo: ${err.message || err}`));
  layoutFrames();
}

// ------------------------------------------------------------------ apply state to the active player
function applyPlayback() {
  if (!S) return;
  const paused = S.effectivePaused;
  const vol = S.volume;
  const muted = S.muted;

  if (kind === 'video') {
    video.volume = vol / 100;
    video.muted = muted;
    video.playbackRate = S.rate;
    video.loop = singleLoop();
    if (paused && !video.paused) video.pause();
    else if (!paused && video.paused && video.getAttribute('src')) video.play().catch(() => {});
  } else if (kind === 'yt' && yt && yt.getPlayerState) {
    applyCaptions(false);
    yt.setVolume(vol);
    if (muted) yt.mute(); else yt.unMute();
    if (yt.getPlaybackRate() !== S.rate) yt.setPlaybackRate(S.rate);
    const st = yt.getPlayerState();
    if (paused && (st === 1 || st === 3)) yt.pauseVideo();
    else if (!paused && (st === 2 || st === 5)) yt.playVideo();
  } else if (kind === 'vimeo' && vimeo) {
    const v = vimeo;
    v.setVolume(muted ? 0 : vol / 100).catch(() => {});
    v.setPlaybackRate(S.rate).catch(() => {});
    const loop = singleLoop();
    if (loop !== vimeoLoop) { vimeoLoop = loop; v.setLoop(loop).catch(() => {}); }
    v.getPaused().then((p) => {
      if (vimeo !== v) return;
      if (paused && !p) v.pause().catch(() => {});
      else if (!paused && p) v.play().catch(() => {});
    }).catch(() => {});
  }
}

/**
 * YouTube turns on (auto-translated) captions by itself depending on the viewer's language;
 * cc_load_policy=0 is not honoured reliably, so unload the captions module explicitly.
 * force: re-apply even if the setting did not change (new video / module reloaded).
 */
let captionsApplied = null;
function applyCaptions(force) {
  if (kind !== 'yt' || !yt || !yt.unloadModule || !S) return;
  const want = !!S.captions;
  if (!force && captionsApplied === want) return;
  captionsApplied = want;
  try {
    if (want) {
      yt.loadModule('captions');
    } else {
      yt.setOption('captions', 'track', {});
      yt.unloadModule('captions');
      yt.unloadModule('cc');
    }
  } catch { /* module not available for this video */ }
}

/** Size embedded iframes according to the fit mode (iframes can't use object-fit). */
function layoutFrames() {
  const W = window.innerWidth; const H = window.innerHeight;
  const fit = S ? S.fit : 'cover';
  for (const [wrap, aspect] of [[ytWrap, ytAspect], [vimeoWrap, vimeoAspect]]) {
    const f = wrap.querySelector('iframe');
    if (!f) continue;
    let w = W; let h = H; let sx = 1; let sy = 1;
    if (fit === 'cover') {
      w = W; h = W / aspect;
      if (h < H) { h = H; w = H * aspect; }
    } else if (fit === 'fill') {
      w = W; h = W / aspect;
      if (h > H) { h = H; w = H * aspect; }
      sx = W / w; sy = H / h;
    }
    f.style.width = `${Math.ceil(w) + 2}px`;
    f.style.height = `${Math.ceil(h) + 2}px`;
    f.style.transform = `translate(-50%, -50%) scale(${sx}, ${sy})`;
  }
}
window.addEventListener('resize', layoutFrames);

// ------------------------------------------------------------------ time / seek
function times() {
  if (kind === 'video') return [video.currentTime, video.duration];
  if (kind === 'yt' && yt && yt.getDuration) return [yt.getCurrentTime(), yt.getDuration()];
  if (kind === 'vimeo') return vimeoTimes;
  return [0, 0];
}

function seekTo(t) {
  const [, d] = times();
  if (!Number.isFinite(d) || d <= 0) return;
  t = Math.max(0, Math.min(d - 0.1, t));
  if (kind === 'video') video.currentTime = t;
  else if (kind === 'yt' && yt) yt.seekTo(t, true);
  else if (kind === 'vimeo' && vimeo) vimeo.setCurrentTime(t).catch(() => {});
}

function updateTime() {
  const [t, d] = times();
  const ok = Number.isFinite(d) && d > 0;
  body.classList.toggle('no-seek', !ok);
  const p = ok ? Math.min(100, (t / d) * 100) : 0;
  $('seek-fill').style.width = `${p}%`;
  $('seek-knob').style.left = `${p}%`;
  $('time').textContent = ok ? `${formatTime(t)} / ${formatTime(d)}` : (cur ? 'ライブ / 不明' : '');
}
setInterval(() => { if (body.classList.contains('hover')) updateTime(); }, 400);

$('seek').addEventListener('pointerdown', (e) => {
  const el = $('seek');
  const at = (ev) => {
    const r = el.getBoundingClientRect();
    const [, d] = times();
    seekTo(((ev.clientX - r.left) / r.width) * d);
    updateTime();
  };
  el.setPointerCapture(e.pointerId);
  at(e);
  el.onpointermove = at;
  el.onpointerup = () => { el.onpointermove = null; el.onpointerup = null; };
});

// ------------------------------------------------------------------ hover / drag / menu
let hoverTimer = null;
function poke() {
  body.classList.add('hover');
  clearTimeout(hoverTimer);
  hoverTimer = setTimeout(() => {
    if (!$('controls').matches(':hover')) body.classList.remove('hover');
  }, 2200);
  updateTime();
}
document.addEventListener('mousemove', poke);
document.addEventListener('mouseleave', () => { clearTimeout(hoverTimer); body.classList.remove('hover'); });

// Grips on the edges resize the window, everything else moves it (done natively by the host).
const endDrag = () => deskflow.send('drag-end');
document.querySelectorAll('.grip').forEach((g) => {
  g.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    e.stopPropagation();
    deskflow.send('drag-start', g.dataset.edge);
  });
  g.addEventListener('pointerup', endDrag);
  g.addEventListener('dblclick', (e) => e.stopPropagation());
});
shield.addEventListener('pointerdown', (e) => {
  if (e.button !== 0) return;
  deskflow.send('drag-start', '');
});
shield.addEventListener('pointerup', endDrag);
shield.addEventListener('dblclick', () => send('togglePause'));

document.addEventListener('contextmenu', (e) => {
  e.preventDefault();
  deskflow.send('show-menu');
});

// ------------------------------------------------------------------ keyboard / paste / drop
document.addEventListener('keydown', (e) => {
  if (!S || e.target === volEl) return;
  const k = e.key;
  if (k === ' ' || k === 'k') { e.preventDefault(); send('togglePause'); }
  else if (k === 'ArrowRight') { const [t] = times(); seekTo(t + 5); }
  else if (k === 'ArrowLeft') { const [t] = times(); seekTo(t - 5); }
  else if (k === 'ArrowUp') send('set', { key: 'volume', value: S.volume + 5 });
  else if (k === 'ArrowDown') send('set', { key: 'volume', value: S.volume - 5 });
  else if (k === 'm' || k === 'M') send('set', { key: 'muted', value: !S.muted });
  else if (k === 'n' || k === 'N') send('next');
  else if (k === 'p' || k === 'P') send('prev');
});

document.addEventListener('paste', (e) => {
  const text = e.clipboardData.getData('text');
  if (text) send('addUrls', { text, playNow: true });
});

let dropDepth = 0;
document.addEventListener('dragenter', (e) => { e.preventDefault(); dropDepth += 1; body.classList.add('dropping'); });
document.addEventListener('dragleave', () => { dropDepth = Math.max(0, dropDepth - 1); if (!dropDepth) body.classList.remove('dropping'); });
document.addEventListener('dragover', (e) => { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; });
document.addEventListener('drop', (e) => {
  e.preventDefault();
  dropDepth = 0;
  body.classList.remove('dropping');
  if (e.dataTransfer.files.length) { deskflow.sendFiles(e.dataTransfer.files, { playNow: true }); return; }
  const text = e.dataTransfer.getData('text/uri-list') || e.dataTransfer.getData('text/plain');
  if (text) send('addUrls', { text, playNow: true });
});
