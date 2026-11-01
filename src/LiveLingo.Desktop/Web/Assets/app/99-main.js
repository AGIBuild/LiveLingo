(function () {
  'use strict';

  // Light or dark follows the system. ponytail: no in-app override yet; add an options.theme field when settings need one.
  const dark = matchMedia('(prefers-color-scheme: dark)');
  const theme = () => {
    document.documentElement.dataset.theme = dark.matches ? 'dark' : 'light';
  };
  theme();
  dark.addEventListener('change', theme);

  LL.mount(LL.Composer);
  new ResizeObserver(LL.measure).observe(document.getElementById('app'));

  // The host injects invokeCSharpAction before load completes; announcing here means "send me strings and state".
  window.addEventListener('load', () => LL.send('ready'));
})();
