// Menus and dialogs live inside the page, anchored to their button, so the window can be sized around them.
(function () {
  'use strict';

  const html = LL.html;
  const Icon = LL.Icon;

  const RADIO = new Set(['target', 'copyOnly', 'clean', 'translate', 'original']);
  const CHECKBOX = new Set(['wide', 'whole']);

  const role = (id) => (RADIO.has(id) ? 'menuitemradio' : CHECKBOX.has(id) ? 'menuitemcheckbox' : 'menuitem');
  const checkable = (id) => RADIO.has(id) || CHECKBOX.has(id);

  class MenuList extends preact.Component {
    setList = (el) => {
      this.list = el;
    };

    componentDidMount() {
      const buttons = this.buttons();
      (this.list.querySelector('[aria-checked="true"]:not(:disabled)') || buttons[0])?.focus();
    }

    buttons() {
      return [...this.list.querySelectorAll('button:not(:disabled)')];
    }

    onKeyDown = (e) => {
      const keys = ['ArrowDown', 'ArrowUp', 'Home', 'End'];
      if (e.key === 'Tab') {
        LL.closeMenu();
        return;
      }
      if (!keys.includes(e.key)) return;
      e.preventDefault();
      const buttons = this.buttons();
      const at = buttons.indexOf(document.activeElement);
      const next = e.key === 'Home' ? 0 : e.key === 'End' ? buttons.length - 1 : (at + (e.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length;
      buttons[next].focus();
    };

    render({ align, label, items, onPick }) {
      return html`<div class=${'menu ' + align} role="menu" aria-label=${label} ref=${this.setList} onKeyDown=${this.onKeyDown}>
        ${items.map((item) => html`${item.sepBefore && html`<hr/>`}<button type="button" key=${item.id} role=${role(item.id)} aria-checked=${checkable(item.id) ? item.checked : undefined}
          disabled=${item.enabled === false} onClick=${() => onPick(item)}><span class="lbl">${item.label}</span>${LL.cap(item.id) && html`<kbd>${LL.cap(item.id)}</kbd>`}</button>`)}
      </div>`;
    }
  }

  LL.Menu = ({ id, align, label, items, onPick }) =>
    LL.state.menu === id ? html`<${MenuList} align=${align} label=${label} items=${items} onPick=${onPick}/>` : null;

  class Dialog extends preact.Component {
    setPane = (el) => {
      this.pane = el;
    };

    componentDidMount() {
      this.pane.querySelector('.dlg-actions button:not(:disabled), .hist button:not(:disabled)')?.focus();
    }

    render({ id, dialogs }) {
      const recent = id === 'recent';
      const body = recent ? html`${dialogs.recent.length === 0
          ? html`<p>${LL.t('recentEmpty')}</p>`
          : html`${dialogs.recent.map((item) => html`<div class="hist" key=${item.index}><span>${item.label}</span>
              <button type="button" class="btn" disabled=${!dialogs.canRestore} onClick=${() => { LL.send('restoreRecent', { index: item.index }); LL.closeDialog(); }}>${LL.t('recentRestore')}</button></div>`)}
            <div class="dlg-actions"><button type="button" class="btn" onClick=${() => LL.send('clearRecent')}>${LL.t('recentClear')}</button></div>`}`
        : html`<p>${LL.t('exportHint')}</p><div class="dlg-actions">${[['txt', 'TXT'], ['markdown', 'Markdown'], ['srt', 'SRT']].map(([format, label]) => html`<button type="button" class="btn" key=${format}
            disabled=${format === 'srt' && !dialogs.export.srt} onClick=${() => { LL.send('export', { format }); LL.closeDialog(); }}>${label}</button>`)}</div>`;
      return html`<section class="dialog" role="dialog" aria-modal="true" aria-labelledby="dlg-title" ref=${this.setPane}>
        <header><h2 id="dlg-title">${LL.t(recent ? 'recentTitle' : 'exportTitle')}</h2>
          <button type="button" id="dlg-close" class="icon-btn" aria-label=${LL.t('closeAria')} onClick=${LL.closeDialog}><${Icon} name="close"/></button></header>
        ${body}
      </section>`;
    }
  }

  LL.Dialog = ({ dialogs }) => (LL.state.dialog ? html`<${Dialog} id=${LL.state.dialog} dialogs=${dialogs}/>` : null);
})();
