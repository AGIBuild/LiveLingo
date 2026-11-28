// Menus and dialogs live inside the page, anchored to their button, so the window can be sized around them.
(function () {
  'use strict';

  const html = LL.html;
  const Icon = LL.Icon;

  const RADIO = new Set(['target', 'copyOnly', 'clean', 'translate', 'original']);
  const CHECKBOX = new Set(['wide', 'whole']);
  const isRadio = (id) => RADIO.has(id) || id.startsWith('polish:');

  const role = (id) => (isRadio(id) ? 'menuitemradio' : CHECKBOX.has(id) ? 'menuitemcheckbox' : 'menuitem');
  const checkable = (id) => isRadio(id) || CHECKBOX.has(id);

  const MenuItem = ({ item, onPick, onFocusItem }) => {
    const cap = LL.cap(item.id);
    return html`${item.sepBefore && html`<hr/>`}<button type="button" key=${item.id}
      title=${item.sub || undefined}
      role=${role(item.id)} aria-checked=${checkable(item.id) ? item.checked : undefined}
      disabled=${item.enabled === false}
      onFocus=${onFocusItem ? () => onFocusItem(item) : undefined}
      onMouseEnter=${onFocusItem ? () => onFocusItem(item) : undefined}
      onClick=${() => onPick(item)}>
      <span class="lbl">${item.label}</span>
      ${cap && html`<kbd aria-hidden="true">${cap}</kbd>`}
    </button>`;
  };

  const defaultSubOf = (items) =>
    (items.find((item) => item.checked && item.sub) || items.find((item) => item.sub))?.sub ?? '';

  class MenuList extends preact.Component {
    constructor(props) {
      super(props);
      this.state = { activeSub: defaultSubOf(props.items) };
    }

    setList = (el) => {
      this.list = el;
    };

    componentDidMount() {
      const buttons = this.buttons();
      (this.list.querySelector('[aria-checked="true"]:not(:disabled)') || buttons[0])?.focus({ preventScroll: true });
      LL.measure?.();
    }

    componentDidUpdate(prevProps) {
      if (prevProps.items !== this.props.items && !this.state.activeSub) {
        const fallback = defaultSubOf(this.props.items);
        if (fallback) this.setState({ activeSub: fallback });
      }
      LL.measure?.();
    }

    buttons() {
      return [...this.list.querySelectorAll('button:not(:disabled)')];
    }

    onFocusItem = (item) => {
      const next = item.sub || defaultSubOf(this.props.items);
      if (this.state.activeSub !== next) this.setState({ activeSub: next });
    };

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

    render({ align, label, items, polishGroup, note, onPick }, { activeSub }) {
      const polishItems = polishGroup ? items.filter((item) => item.group === 'polish') : [];
      const restItems = polishGroup ? items.filter((item) => item.group !== 'polish') : items;
      return html`<div class=${'menu ' + align + (polishItems.length ? ' output-menu' : '')} role="menu" aria-label=${label} ref=${this.setList} onKeyDown=${this.onKeyDown}>
        ${polishItems.length > 0 && html`<div class="menu-group" role="group" aria-label=${polishGroup}>
          <p class="menu-group-head" aria-hidden="true">${polishGroup}</p>
          ${polishItems.map((item) => html`<${MenuItem} key=${item.id} item=${item} onFocusItem=${this.onFocusItem} onPick=${onPick}/>`)}
        </div><hr/>`}
        ${restItems.map((item) => html`<${MenuItem} key=${item.id} item=${item} onFocusItem=${polishItems.length ? this.onFocusItem : null} onPick=${onPick}/>`)}
        ${activeSub && html`<p class="menu-sub-preview" aria-hidden="true">${activeSub}</p>`}
        ${note && html`<p class="menu-note">${note}</p>`}
      </div>`;
    }
  }

  LL.Menu = ({ id, align, label, items, polishGroup, note, onPick }) =>
    LL.state.menu === id ? html`<${MenuList} align=${align} label=${label} items=${items} polishGroup=${polishGroup} note=${note} onPick=${onPick}/>` : null;

  // What the privacy chip opens: the model of each stage as this window actually used it. Nothing to pick, so it is a
  // card under the chip, not a menu; like a menu it closes on Escape and when the pointer goes anywhere else.
  LL.ModelCard = ({ rows }) => html`<section class="menu right model-card" aria-label=${LL.t('modelCardTitle')}>
    <h3>${LL.t('modelCardTitle')}</h3>
    <dl>${rows.map((row) => html`<div key=${row.label}><dt>${row.label}</dt><dd>${row.text}</dd></div>`)}</dl>
  </section>`;

  class Dialog extends preact.Component {
    setPane = (el) => {
      this.pane = el;
    };

    componentDidMount() {
      const pane = this.pane;
      (pane.querySelector('.dlg-actions .last:not(:disabled)') || pane.querySelector('.dlg-actions button:not(:disabled), .hist button:not(:disabled)'))?.focus();
    }

    render({ id, dialogs }) {
      const recent = id === 'recent';
      const body = recent ? html`${dialogs.recent.length === 0
          ? html`<p>${LL.t('recentEmpty')}</p>`
          : html`${dialogs.recent.map((item) => html`<div class="hist" key=${item.index}><span>${item.label}</span>
              <button type="button" class="btn" disabled=${!dialogs.canRestore} onClick=${() => { LL.send('restoreRecent', { index: item.index }); LL.closeDialog(); }}>${LL.t('recentRestore')}</button></div>`)}
            <div class="dlg-actions"><button type="button" class="btn" onClick=${() => LL.send('clearRecent')}>${LL.t('recentClear')}</button></div>`}`
        : html`<p>${LL.t('exportHint')}</p><div class="dlg-actions">${[['txt', 'TXT'], ['markdown', 'Markdown'], ['srt', 'SRT']].map(([format, label]) => html`<button type="button" class=${'btn' + (format === dialogs.export.last ? ' last' : '')} key=${format}
            aria-current=${format === dialogs.export.last ? 'true' : undefined} disabled=${format === 'srt' && !dialogs.export.srt} onClick=${() => { LL.send('export', { format }); LL.closeDialog(); }}>${label}</button>`)}</div>`;
      return html`<section class="dialog" role="dialog" aria-modal="true" aria-labelledby="dlg-title" ref=${this.setPane}>
        <header><h2 id="dlg-title">${LL.t(recent ? 'recentTitle' : 'exportTitle')}</h2>
          <button type="button" id="dlg-close" class="icon-btn" aria-label=${LL.t('closeAria')} onClick=${LL.closeDialog}><${Icon} name="close"/></button></header>
        ${body}
      </section>`;
    }
  }

  LL.Dialog = ({ dialogs }) => (LL.state.dialog ? html`<${Dialog} id=${LL.state.dialog} dialogs=${dialogs}/>` : null);
})();
