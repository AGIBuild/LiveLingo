// Header, banner and footer of the composer. Every text comes from the snapshot or from the strings message.
(function () {
  'use strict';

  const html = LL.html;

  LL.Icon = ({ name }) => html`<svg class="ic" aria-hidden="true"><use href=${'#i-' + name}/></svg>`;
  const Icon = LL.Icon;

  const toggle = (id) => (e) => {
    if (LL.state.menu === id) LL.closeMenu();
    else LL.openMenu(id, e.currentTarget);
  };

  const pickTarget = (item) => {
    LL.send('copyOnly', { on: item.id === 'copyOnly' });
    LL.closeMenu();
  };

  const pickOutput = (item) => {
    if (item.id === 'swap') LL.send('swapLanguage');
    else LL.send('output', { value: item.id });
    LL.closeMenu();
  };

  // The switches stay open so several can be changed in a row, like the prototype.
  const pickMore = (item) => {
    switch (item.id) {
      case 'recent':
        LL.openDialog('recent');
        return;
      case 'export':
        LL.openDialog('export');
        return;
      case 'wide':
        LL.send('wide', { on: !item.checked });
        return;
      case 'whole':
        LL.send('whole', { on: !item.checked });
        return;
      case 'settings':
        LL.send('openSettings', { section: 'general' });
        break;
      default:
        LL.send(item.id === 'new' ? 'newDraft' : item.id);
    }
    LL.closeMenu();
  };

  LL.Header = ({ c }) => {
    const open = LL.state.menu;
    const dest = c.dest;
    const output = [...c.output.menu, { id: 'swap', label: c.output.swap, sepBefore: true }];
    const more = c.menus.more.map((item) => (item.id === 'wide' || item.id === 'settings' ? { ...item, sepBefore: true } : item));
    return html`<header class="c-head">
      <div class="anchor">
        <button type="button" id="dest" class=${'chip dest' + (dest.kind === 'lost' ? ' bad' : dest.kind === 'copyOnly' ? ' warn' : '')}
          aria-label=${LL.t('destAria')} aria-haspopup="menu" aria-expanded=${open === 'target'} onClick=${toggle('target')}>
          ${dest.kind !== 'copyOnly' && html`<${Icon} name=${dest.kind === 'lost' ? 'alert' : 'target'}/>`}${dest.text}</button>
        <${LL.Menu} id="target" align="left" label=${LL.t('destAria')} items=${c.menus.target} onPick=${pickTarget}/>
      </div>
      <span class="spacer"></span>
      <span id="privacy" class=${'chip privacy ' + (c.privacy.cloud ? 'warn' : 'ok')} role="img" aria-label=${c.privacy.aria}>
        <i class="dot"></i><span class="lbl">${c.privacy.text}</span></span>
      <div class="anchor">
        <button type="button" id="out-pill" class="pill" aria-label=${LL.t('outputAria')} aria-haspopup="menu" aria-expanded=${open === 'output'}
          onClick=${toggle('output')}>${c.output.globe && html`<${Icon} name="globe"/>`}${c.output.pill}<${Icon} name="chevron"/></button>
        <${LL.Menu} id="output" align="right" label=${LL.t('outputAria')} items=${output} note=${c.output.note} onPick=${pickOutput}/>
      </div>
      <div class="anchor">
        <button type="button" id="more" class="icon-btn" aria-label=${LL.t('moreAria')} title=${LL.t('moreAria')} aria-haspopup="menu"
          aria-expanded=${open === 'more'} onClick=${toggle('more')}><${Icon} name="more"/></button>
        <${LL.Menu} id="more" align="right" label=${LL.t('moreAria')} items=${more} onPick=${pickMore}/>
      </div>
      <button type="button" id="zoom" class="icon-btn" aria-label=${LL.t(LL.state.fixed ? 'unzoomAria' : 'zoomAria')}
        title=${LL.t(LL.state.fixed ? 'unzoomAria' : 'zoomAria') + ' (' + LL.cap('zoom') + ')'} onClick=${() => LL.send('zoom')}>
        <${Icon} name=${LL.state.fixed ? 'shrink' : 'expand'}/></button>
      <button type="button" id="close" class="icon-btn" aria-label=${LL.t('closeAria')} title=${LL.t('closeAria')}
        onClick=${() => LL.send('hide')}><${Icon} name="close"/></button>
    </header>`;
  };

  LL.Banner = ({ b }) => {
    if (!b) return null;
    return html`<div id="banner" class=${'banner ' + b.tone} role=${b.tone === 'info' ? 'status' : 'alert'}>
      <${Icon} name=${b.tone === 'info' ? 'info' : 'alert'}/>
      <p>${b.message}</p>
      ${b.kind === 'downloading' && html`<progress max="100" value=${b.progress} aria-label=${LL.t('progressAria')}></progress>`}
      <span class="actions">${b.actions.map((a) => html`<button type="button" key=${a.id} onClick=${() => LL.send('banner', { action: a.id })}>${a.label}</button>`)}</span>
    </div>`;
  };

  // The key cap of a shortcut (snapshot options), for menus and tooltips; "" when the shortcut does not exist.
  LL.cap = (id) => LL.state.composer?.options.shortcuts.find((s) => s.id === id)?.cap ?? '';

  LL.Footer = ({ c }) => {
    const f = c.footer;
    const cm = f.commit;
    return html`<footer class="c-foot">
      <button type="button" id="mic" class=${'mic' + (f.mic.recording ? ' recording' : '')} aria-label=${f.mic.aria} title=${f.mic.aria}
        onClick=${() => LL.send('micToggle')}>
        <svg class="ic mic-ic" aria-hidden="true"><use href="#i-mic"/></svg>
        <span class="bars" aria-hidden="true"><i></i><i></i><i></i><i></i><i></i></span>
      </button>
      <p id="status" class=${'status' + (f.status.tone === 'warn' ? ' warn' : '')} role="status" aria-live="polite">${f.status.text}</p>
      <button type="button" id="copy" class="icon-btn" aria-label=${LL.t('copyAria')} title=${LL.t('copyAria') + ' (' + LL.cap('copy') + ')'} disabled=${!f.copyEnabled}
        onClick=${() => LL.send('copy')}><${Icon} name="copy"/></button>
      <button type="button" id="commit" class="btn primary" disabled=${!cm.enabled} aria-label=${cm.aria}
        onClick=${() => LL.send('commit', { fromKey: false })}>${cm.armed
          ? html`<span class="spinner" aria-hidden="true"></span>${cm.label}`
          : html`${cm.label}${!cm.written && html`<kbd>${c.options.commitKeyCap}</kbd>`}`}</button>
    </footer>`;
  };
})();
