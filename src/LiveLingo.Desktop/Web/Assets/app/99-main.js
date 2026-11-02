(function () {
  'use strict';

  // One bundle, two windows: the host says which screen this page is (WebPage.Build).
  const composer = document.documentElement.dataset.entry === 'composer';
  LL.mount(composer ? LL.Composer : LL.Settings);
  // Only the composer's window follows its content; the settings window has a fixed size.
  if (composer) new ResizeObserver(LL.measure).observe(document.getElementById('app'));

  // The host injects invokeCSharpAction before load completes; announcing here means "send me strings and state".
  window.addEventListener('load', () => LL.send('ready'));
})();
