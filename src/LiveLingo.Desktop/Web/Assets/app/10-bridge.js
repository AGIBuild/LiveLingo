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
    LL.send('size', { width, height });
  }

  LL.measure = () => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(measure);
  };

  LL.resetMeasure = () => {
    lastSize = '';
  };
})();
