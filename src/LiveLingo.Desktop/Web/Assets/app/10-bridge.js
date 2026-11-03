// Page to C#: a closed set of intents (design 5.1). The host injects invokeCSharpAction into pages it loads.
(function () {
  'use strict';

  const VERSION = 1;
  const MARGIN = 18;

  // An intent carries what the user did, never a decision: C# validates it and answers with the next snapshot.
  LL.send = (name, fields) => {
    void window.invokeCSharpAction(JSON.stringify({ v: VERSION, name, ...fields }));
  };

  // Content size in CSS pixels; the window follows it. Only changes are sent.
  let lastSize = '';
  let queued = false;
  let content = { width: 0, height: 0 };
  let clipTimer = 0;
  let clippedKey = '';

  // A page whose visible area stays smaller than its content is cut off by the window: the host cannot see that from
  // the sizes it was told, so the page reports it (once per distinct case) for the log. The wait lets a window that is
  // being resized settle first.
  function watchClipping() {
    clearTimeout(clipTimer);
    clipTimer = setTimeout(() => {
      const viewport = { width: window.innerWidth, height: window.innerHeight };
      if (viewport.width + 1 >= content.width && viewport.height + 1 >= content.height) {
        clippedKey = '';
        return;
      }
      const key = `${viewport.width}x${viewport.height}/${content.width}x${content.height}`;
      if (key === clippedKey) return;
      clippedKey = key;
      LL.send('clipped', {
        viewportWidth: viewport.width, viewportHeight: viewport.height,
        contentWidth: content.width, contentHeight: content.height
      });
    }, 300);
  }

  function measure() {
    queued = false;
    const app = document.getElementById('app');
    if (!app) return;
    const rect = app.getBoundingClientRect();
    let bottom = rect.bottom;
    // A menu hanging below the panel needs room too: nothing clips it but the window.
    document.querySelectorAll('.menu').forEach((menu) => {
      bottom = Math.max(bottom, menu.getBoundingClientRect().bottom);
    });
    // The page keeps an 18 px margin all around for the panel's shadow.
    const width = Math.ceil(rect.width + 2 * MARGIN);
    const height = Math.ceil(bottom + MARGIN);
    const key = width + 'x' + height;
    if (key === lastSize || width < 1 || height < 1) return;
    lastSize = key;
    content = { width, height };
    watchClipping();
    LL.send('size', { width, height });
  }

  window.addEventListener('resize', () => {
    if (content.width > 0) watchClipping();
  });

  LL.measure = () => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(measure);
  };
})();
