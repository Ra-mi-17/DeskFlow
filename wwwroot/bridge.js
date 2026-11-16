'use strict';
// Bridge between the pages and the .NET host (WebView2 postMessage).
(() => {
  const wv = window.chrome && window.chrome.webview;
  const listeners = new Map();
  const pending = new Map();
  let seq = 0;

  if (wv) {
    wv.addEventListener('message', (e) => {
      const m = e.data || {};
      if (typeof m.reply === 'number') {
        const resolve = pending.get(m.reply);
        if (resolve) { pending.delete(m.reply); resolve(m.data); }
        return;
      }
      for (const cb of listeners.get(m.ch) || []) {
        try { cb(m.data); } catch (err) { console.error(err); }
      }
    });
  }

  window.deskflow = {
    send(ch, data) { if (wv) wv.postMessage({ ch, data }); },
    invoke(ch, data) {
      return new Promise((resolve) => {
        if (!wv) { resolve(null); return; }
        seq += 1;
        pending.set(seq, resolve);
        wv.postMessage({ ch, data, id: seq });
      });
    },
    on(ch, cb) {
      if (!listeners.has(ch)) listeners.set(ch, []);
      listeners.get(ch).push(cb);
    },
    /** Send dropped File objects; the host receives their real paths. */
    sendFiles(files, data) {
      if (wv && files && files.length) wv.postMessageWithAdditionalObjects({ ch: 'drop-files', data }, files);
    },
  };
})();
