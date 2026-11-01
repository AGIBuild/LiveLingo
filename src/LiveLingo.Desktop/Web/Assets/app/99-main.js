// Placeholder entry (W1). The composer and settings views replace it in W3 / W5.
(function () {
  'use strict';
  const html = htm.bind(preact.h);
  const root = document.getElementById('app');
  let rev = 0;

  function paint() {
    preact.render(html`<p id="rev">rev ${rev}</p>`, root);
  }

  // The host pushes JSON strings through LL.receive via InvokeScript.
  window.LL = {
    receive(json) {
      rev = JSON.parse(json).rev;
      paint();
    },
  };
  paint();
})();
