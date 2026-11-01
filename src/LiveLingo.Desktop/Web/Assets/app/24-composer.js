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
      document.documentElement.dataset.motion = c.options.animations ? '' : 'off';
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
})();
