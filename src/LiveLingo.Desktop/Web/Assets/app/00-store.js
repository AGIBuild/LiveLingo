// The page's only state: the latest snapshot pushed by C# plus a few transient UI facts (open menu or dialog, toast).
// Business state never lives here; everything the user does leaves as an intent (10-bridge.js).
(function () {
  'use strict';

  const TOAST_MS = 3600;
  const LL = (window.LL = {});

  LL.html = htm.bind(preact.h);
  LL.state = {
    strings: {},
    composer: null,
    rev: 0,
    // The revision of the last text the page sent or adopted (design 5.3); a snapshot overwrites the text box only when newer.
    sentRev: 0,
    menu: null,
    dialog: null,
    toast: '',
    focus: null,
  };

  let root = null;
  let toastTimer = 0;
  let focusCount = 0;

  LL.t = (key) => LL.state.strings[key] ?? '';

  LL.mount = (component) => {
    root = () => preact.render(preact.h(component, null), document.getElementById('app'));
    root();
  };

  LL.update = () => root && root();

  // Focus goes back to what opened a menu or dialog when it closes. WebKit does not focus a button on click,
  // so the opener is the clicked element, not document.activeElement.
  let opener = null;

  const restore = () => {
    opener?.focus();
    opener = null;
  };

  LL.openMenu = (id, from) => {
    opener = from ?? document.activeElement;
    LL.state.menu = id;
    LL.update();
  };

  LL.closeMenu = () => {
    LL.state.menu = null;
    LL.update();
    restore();
  };

  LL.openDialog = (id) => {
    opener ??= document.activeElement; // a dialog opened from a menu keeps the menu's opener
    LL.state.menu = null;
    LL.state.dialog = id;
    LL.update();
  };

  LL.closeDialog = () => {
    LL.state.dialog = null;
    LL.update();
    restore();
  };

  function toast(text) {
    LL.state.toast = text;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      LL.state.toast = '';
      LL.update();
    }, TOAST_MS);
  }

  // Entry point of the host: every message is one JSON string with the envelope { v, type, data }.
  LL.receive = (json) => {
    const message = JSON.parse(json);
    const s = LL.state;
    switch (message.type) {
      case 'strings':
        s.strings = message.data.strings;
        break;
      case 'composer':
        if (message.data.rev <= s.rev) return; // a late or repeated snapshot
        s.rev = message.data.rev;
        s.composer = message.data;
        break;
      case 'toast':
        toast(message.data.text);
        break;
      case 'focus':
        s.focus = { target: message.data.target, n: ++focusCount };
        break;
      case 'openMenu':
        LL.openMenu(message.data.id);
        return;
      case 'reset':
        s.menu = null;
        s.dialog = null;
        s.toast = '';
        opener = null;
        break;
      default:
        throw new Error('Unknown message type: ' + message.type);
    }
    LL.update();
  };
})();
