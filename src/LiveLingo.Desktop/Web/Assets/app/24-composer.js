// The composer panel and its keyboard rules. The same rules as the prototype; the page only reports keys, C# decides.
(function () {
  'use strict';

  const html = LL.html;

  class Composer extends preact.Component {
    focusHandled = 0;

    componentDidMount() {
      this.after();
    }

    componentDidUpdate() {
      this.after();
    }

    after() {
      const { composer: c, focus } = LL.state;
      if (!c) return;
      if (focus && focus.n !== this.focusHandled) {
        this.focusHandled = focus.n;
        focusTarget(focus.target);
      }
      LL.measure();
    }

    render() {
      const { composer: c, dialog, toast } = LL.state;
      if (!c) return null;
      const cls = 'composer' + (c.layout.wide ? ' wide' : '') + (c.result.visible ? ' has-res' : '') + (dialog ? ' dialog-open' : '');
      return html`<section id="composer" class=${cls} aria-label="LiveLingo">
        <${LL.Header} c=${c}/>
        <${LL.Banner} b=${c.banner}/>
        <div class="c-body">
          <${LL.Source} source=${c.source} options=${c.options}/>
          <${LL.Result} result=${c.result} options=${c.options}/>
        </div>
        <${LL.Footer} c=${c}/>
        <${LL.Dialog} dialogs=${c.dialogs}/>
        ${toast && html`<div id="toast" class="toast" role="status">${toast}</div>`}
      </section>`;
    }
  }

  LL.Composer = Composer;

  function focusTarget(target) {
    const source = document.getElementById('src');
    const element = target === 'edit' ? document.querySelector('.edit') : target === 'source' && !source.hidden ? source : document.getElementById('mic');
    element?.focus();
  }

  // Keys that are part of an input method composition are never commands. WKWebView reports the confirming Enter with
  // isComposing false and keyCode 229 (measured in W0), so both are checked, here and nowhere else.
  const composing = (e) => e.isComposing || e.keyCode === 229;

  function commitChord(e, commitKey) {
    const ctrl = e.ctrlKey || e.metaKey;
    return commitKey === 'ctrlEnter' ? ctrl : !ctrl && !e.altKey;
  }

  // The shortcuts are defined once, in C#, and arrive with the snapshot: Command (macOS) or Ctrl plus a physical key,
  // so they work with any keyboard layout and while an input method is active.
  function shortcutOf(e, options) {
    const mod = options.modifier === 'meta' ? e.metaKey && !e.ctrlKey : e.ctrlKey && !e.metaKey;
    if (!mod || e.altKey) return null;
    return options.shortcuts.find((s) => s.code === e.code && s.shift === e.shiftKey) ?? null;
  }

  // What a shortcut does is what the button or menu item of the same id does; an action that is not available
  // (nothing to copy, an output the settings do not offer) is simply not run.
  function runShortcut(id, c) {
    const offered = (output) => c.output.menu.some((item) => item.id === output);
    switch (id) {
      case 'dictate': LL.send('micToggle'); break;
      case 'clean':
      case 'translate':
      case 'original': if (offered(id)) LL.send('output', { value: id }); break;
      case 'swap': LL.send('swapLanguage'); break;
      case 'copy': if (c.footer.copyEnabled) LL.send('copy'); break;
      case 'new': LL.send('newDraft'); break;
      case 'recent': LL.openDialog('recent'); break;
      case 'import': if (c.menus.more.find((item) => item.id === 'import').enabled) LL.send('import'); break;
      case 'export': LL.openDialog('export'); break;
      case 'settings': LL.send('openSettings', { section: 'general' }); break;
      default: throw new Error('Unknown shortcut: ' + id);
    }
  }

  document.addEventListener('keydown', (e) => {
    const { composer: c, menu, dialog } = LL.state;
    if (!c || composing(e)) return;

    if (dialog) {
      if (e.key === 'Escape') {
        e.preventDefault();
        LL.closeDialog();
      }
      return;
    }

    const shortcut = shortcutOf(e, c.options);
    if (shortcut) {
      e.preventDefault();
      if (menu) LL.closeMenu();
      runShortcut(shortcut.id, c);
      return;
    }

    if (menu) {
      if (e.key === 'Escape') {
        e.preventDefault();
        LL.closeMenu();
      }
      return;
    }
    if (e.key === 'Escape') {
      e.preventDefault();
      LL.send('cancel');
      return;
    }
    if (e.key !== 'Enter' || e.shiftKey) return;

    const target = e.target;
    if (target.classList?.contains('edit')) {
      e.preventDefault();
      LL.send('saveEdit');
      return;
    }
    if (!(target.id === 'src' || target === document.body || target.id === 'mic')) return;
    if (commitChord(e, c.options.commitKey)) {
      e.preventDefault();
      LL.send('commit', { fromKey: true });
    }
  });

  // A menu closes when the pointer goes anywhere else, like a popover.
  document.addEventListener('pointerdown', (e) => {
    if (LL.state.menu && !e.target.closest('.menu, [aria-haspopup]')) LL.closeMenu();
  });

  // The header is the window's handle. A native window cannot be moved by its page and the pointer's screen
  // coordinates are not reliable across web engines, so the page only says the drag began (the button is held and the
  // pointer moved a few pixels) and the system moves the window.
  const DRAG_THRESHOLD = 4;
  let press = null;

  document.addEventListener('pointerdown', (e) => {
    press = null;
    if (e.button !== 0 || !(e.target instanceof Element)) return;
    if (!e.target.closest('.c-head') || e.target.closest('button, .menu, [aria-haspopup]')) return;
    press = { id: e.pointerId, x: e.clientX, y: e.clientY };
  });

  document.addEventListener('pointermove', (e) => {
    if (press?.id !== e.pointerId) return;
    if ((e.buttons & 1) === 0) {
      press = null;
      return;
    }
    if (Math.hypot(e.clientX - press.x, e.clientY - press.y) < DRAG_THRESHOLD) return;
    press = null;
    LL.send('drag');
  });

  const endPress = () => {
    press = null;
  };
  document.addEventListener('pointerup', endPress);
  document.addEventListener('pointercancel', endPress);
})();
