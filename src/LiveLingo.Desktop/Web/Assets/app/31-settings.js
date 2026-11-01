// The settings page: four groups, every change is applied at once (there is no save button). The page only draws the
// snapshot and reports intents; C# validates and stores.
(function () {
  'use strict';

  const html = LL.html;
  const { Row, Select, Switch, TextField, KeyField, HotkeyField } = LL;

  const SECTIONS = [['general', 'nav.general'], ['voice', 'nav.voice'], ['privacy', 'nav.privacy'], ['models', 'nav.models']];

  const set = (key) => (value) => LL.send('setting', { key, value });

  // Menu labels follow the interface language, except the names of the languages themselves (as in the composer).
  const choices = (values, labelKeys) => values.map((value, i) => [value, LL.t(labelKeys[i])]);

  const General = ({ g }) => html`<div>
    <${Row} id="hotkey" title=${LL.t('hotkey')} desc=${LL.t('hotkey.desc')}
      extra=${g.hotkeyInvalid && html`<p class="err" role="alert">${LL.t('hotkey.invalid')}</p>`}>
      <${HotkeyField} id="hotkey" value=${g.hotkey} onChange=${set('hotkey')}/>
    <//>
    <${Row} id="commit" title=${LL.t('commit')} desc=${LL.t('commit.desc')}>
      <${Select} id="commit" value=${g.commitKey} options=${choices(['Enter', 'CtrlEnter'], ['commit.enter', 'commit.ctrlEnter'])} onChange=${set('commitKey')}/>
    <//>
    <${Row} id="language" title=${LL.t('language')} desc=${LL.t('language.desc')}>
      <${Select} id="language" value=${g.language} options=${[['zh-CN', '中文'], ['en-US', 'English']]} onChange=${set('language')}/>
    <//>
    <${Row} id="theme" title=${LL.t('theme')} desc=${LL.t('theme.desc')}>
      <${Select} id="theme" value=${g.theme} options=${choices(['Auto', 'Light', 'Dark'], ['theme.auto', 'theme.light', 'theme.dark'])} onChange=${set('theme')}/>
    <//>
    <${Row} id="motion" title=${LL.t('motion')} desc=${LL.t('motion.desc')}>
      <${Switch} id="motion" checked=${g.reduceMotion} onChange=${(on) => set('reduceMotion')(String(on))}/>
    <//>
  </div>`;

  class Terms extends preact.Component {
    state = { source: '', target: '' };

    add = () => {
      const { source, target } = this.state;
      if (!source.trim()) return;
      this.setState({ source: '', target: '' });
      LL.send('addTerm', { source, target });
    };

    onKeyDown = (e) => {
      // An input method confirms its candidate with Enter (keyCode 229 in WKWebView); that is not "add".
      if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) {
        e.preventDefault();
        this.add();
      }
    };

    render({ terms }, { source, target }) {
      return html`<section class="terms" aria-labelledby="terms-title">
        <h2 id="terms-title">${LL.t('terms')}</h2>
        <div class="term-form">
          <label>${LL.t('terms.source')}<input id="term-source" value=${source} placeholder=${LL.t('terms.sourceExample')} spellcheck="false"
            onInput=${(e) => this.setState({ source: e.target.value })} onKeyDown=${this.onKeyDown}/></label>
          <label>${LL.t('terms.target')}<input id="term-target" value=${target} placeholder=${LL.t('terms.targetExample')} spellcheck="false"
            onInput=${(e) => this.setState({ target: e.target.value })} onKeyDown=${this.onKeyDown}/></label>
          <button type="button" id="term-add" class="btn" disabled=${!source.trim()} onClick=${this.add}>${LL.t('terms.add')}</button>
        </div>
        <p class="hint">${LL.t('terms.hint')}</p>
        <ul class="term-list">${terms.map((term) => html`<li class="term" key=${term.i}>
          <span>${term.source}</span><span class="arrow" aria-hidden="true">→</span><span>${term.target}</span>
          <button type="button" class="danger" aria-label=${LL.t('terms.remove') + ' ' + term.source}
            onClick=${() => LL.send('removeTerm', { index: term.i })}>${LL.t('terms.remove')}</button>
        </li>`)}</ul>
      </section>`;
    }
  }

  const Voice = ({ v }) => html`<div>
    <${Row} id="mic" title=${LL.t('mic')} desc=${LL.t('mic.desc')}>
      <${Select} id="mic" value=${v.microphone} options=${v.microphones.map((name) => [name, name || LL.t('mic.default')])} onChange=${set('microphone')}/>
    <//>
    <${Row} id="cleanup" title=${LL.t('cleanup')} desc=${LL.t('cleanup.desc')}>
      <${Switch} id="cleanup" checked=${v.cleanup} onChange=${(on) => set('cleanup')(String(on))}/>
    <//>
    <${Row} id="direction" title=${LL.t('direction')} desc=${LL.t('direction.desc')}>
      <${Select} id="direction" value=${v.direction} options=${choices(['English', 'Chinese'], ['direction.zhEn', 'direction.enZh'])} onChange=${set('direction')}/>
    <//>
    <${Terms} terms=${v.terms}/>
  </div>`;

  const cloudToggle = (step) => (on) => LL.send('cloudToggle', { step, on });

  const Privacy = ({ p }) => html`<div>
    <${Row} id="cloud-translate" title=${LL.t('cloud.translate')} desc=${LL.t('cloud.translate.desc')}>
      <${Switch} id="cloud-translate" checked=${p.cloudTranslate} onChange=${cloudToggle('translate')}/>
    <//>
    <${Row} id="cloud-clean" title=${LL.t('cloud.clean')} desc=${LL.t('cloud.clean.desc')}>
      <${Switch} id="cloud-clean" checked=${p.cloudClean} onChange=${cloudToggle('clean')}/>
    <//>
    <${Row} id="cloud-url" title=${LL.t('cloud.baseUrl')} desc=${LL.t('cloud.baseUrl.desc')}>
      <${TextField} id="cloud-url" value=${p.baseUrl} placeholder="https://…/v1" onChange=${set('cloudBaseUrl')}/>
    <//>
    <${Row} id="cloud-model" title=${LL.t('cloud.model')} desc=${LL.t('cloud.model.desc')}>
      <${TextField} id="cloud-model" value=${p.model} onChange=${set('cloudModel')}/>
    <//>
    <${Row} id="cloud-key" title=${LL.t('cloud.apiKey')} desc=${LL.t('cloud.apiKey.desc')}
      extra=${html`${p.hasKey && html`<p class="stored"><span>${LL.t('cloud.keyStored')}</span>
          <button type="button" id="forget-key" class="danger" onClick=${() => LL.send('forgetKey')}>${LL.t('cloud.forget')}</button></p>`}
        ${p.invalid && html`<p class="err" role="alert">${LL.t('cloud.invalid')}</p>`}`}>
      <${KeyField} id="cloud-key"/>
    <//>
    <${Row} id="recent-clear" title=${LL.t('recent')} desc=${LL.t('recent.desc')}>
      <button type="button" id="recent-clear" class="btn" onClick=${() => LL.send('clearRecent')}>${LL.t('recent.clear')}</button>
    <//>
  </div>`;

  // A modal dialog: keeps the focus inside, closes on Escape and gives the focus back to what opened it.
  // The child marked data-initial takes the focus first; it is the safe answer.
  class Modal extends preact.Component {
    componentDidMount() {
      this.pane.querySelector('[data-initial]').focus();
      document.addEventListener('keydown', this.onKeyDown);
    }

    componentWillUnmount() {
      document.removeEventListener('keydown', this.onKeyDown);
      document.getElementById(this.props.returnTo)?.focus();
    }

    onKeyDown = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        this.props.onClose();
      } else if (e.key === 'Tab') {
        const buttons = [...this.pane.querySelectorAll('button')];
        const at = buttons.indexOf(document.activeElement);
        const next = (at + (e.shiftKey ? -1 : 1) + buttons.length) % buttons.length;
        e.preventDefault();
        buttons[next].focus();
      }
    };

    render({ id, title, onClose, children }) {
      return html`<div class="scrim">
        <section class="dialog" role="dialog" aria-modal="true" aria-labelledby=${id + '-title'} ref=${(el) => (this.pane = el)}>
          <header><h2 id=${id + '-title'}>${title}</h2>
            <button type="button" class="icon-btn" aria-label=${LL.t('closeAria')} onClick=${onClose}><${LL.Icon} name="close"/></button></header>
          ${children}
        </section>
      </div>`;
    }
  }

  class Diagnostics extends preact.Component {
    state = { open: false };

    show = () => this.setState({ open: true });

    hide = () => this.setState({ open: false });

    render({ lines }, { open }) {
      return html`<${Row} id="diag-open" title=${LL.t('diag')} desc=${LL.t('diag.desc')}>
        <button type="button" id="diag-open" class="btn" onClick=${this.show}>${LL.t('diag.open')}</button>
      <//>
      ${open && html`<${Modal} id="diag" title=${LL.t('diag.title')} returnTo="diag-open" onClose=${this.hide}>
        <dl id="diag-text" class="diag">${lines.map((line) => html`<div key=${line.label}><dt>${line.label}</dt><dd>${line.value}</dd></div>`)}</dl>
        <div class="dlg-actions"><button type="button" id="diag-close" class="btn primary" data-initial onClick=${this.hide}>${LL.t('closeAria')}</button></div>
      <//>`}`;
    }
  }

  const Models = ({ m }) => html`<div>
    <div class="chips">${['models.speech', 'models.clean', 'models.translate'].map((key) => html`<span class="chip" key=${key}>${LL.t(key)}</span>`)}</div>
    <${Row} id="tier" title=${LL.t('model')} desc=${LL.t('model.desc')}>
      <${Select} id="tier" value=${m.tier} options=${choices(['Quality', 'Fast'], ['model.quality', 'model.fast'])} onChange=${set('tier')}/>
    <//>
    ${m.legacy.count > 0 && html`<${Row} id="legacy-remove" title=${LL.t('legacy')} desc=${m.legacy.text}>
      <button type="button" id="legacy-remove" class="btn" onClick=${() => LL.send('removeLegacyModels')}>${LL.t('legacy.remove')}</button>
    <//>`}
    <${Diagnostics} lines=${m.diagnostics}/>
    <${Row} id="version" title=${LL.t('version')} desc=${LL.t('version.desc')}>
      <span id="version" class="chip">${m.version}</span>
    <//>
  </div>`;

  // Turning a cloud step on needs the user's consent every time. C# says when to ask; the safe answer is the first button.
  const answer = (allow) => LL.send('consent', { allow });

  const Consent = ({ step }) => html`<${Modal} id="consent" title=${LL.t('consent.' + step)} returnTo=${'cloud-' + step} onClose=${() => answer(false)}>
    <p>${LL.t('consent.body')}</p>
    <div class="dlg-actions">
      <button type="button" id="consent-keep" class="btn" data-initial onClick=${() => answer(false)}>${LL.t('consent.keepLocal')}</button>
      <button type="button" id="consent-allow" class="btn primary" onClick=${() => answer(true)}>${LL.t('consent.allow')}</button>
    </div>
  <//>`;

  LL.Settings = () => {
    const s = LL.state.settings;
    if (!s) return null;
    return html`<div class="settings">
      <nav aria-label=${LL.t('nav.aria')}>${SECTIONS.map(([id, key]) => html`<button type="button" key=${id} id=${'nav-' + id}
        aria-current=${s.section === id ? 'page' : undefined} onClick=${() => LL.send('openSettings', { section: id })}>${LL.t(key)}</button>`)}</nav>
      <main id="panel">
        ${s.section === 'general' && html`<${General} g=${s.general}/>`}
        ${s.section === 'voice' && html`<${Voice} v=${s.voice}/>`}
        ${s.section === 'privacy' && html`<${Privacy} p=${s.privacy}/>`}
        ${s.section === 'models' && html`<${Models} m=${s.models}/>`}
      </main>
      ${s.privacy.consent && html`<${Consent} step=${s.privacy.consent}/>`}
    </div>`;
  };
})();
