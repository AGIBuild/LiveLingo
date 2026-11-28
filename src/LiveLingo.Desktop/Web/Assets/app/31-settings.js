// The settings page: four groups, every change is applied at once (there is no save button). The page only draws the
// snapshot and reports intents; C# validates and stores.
(function () {
  'use strict';

  const html = LL.html;
  const { Row, Select, Switch, HotkeyField } = LL;

  const SECTIONS = [
    ['general', 'nav.general'],
    ['voice', 'nav.voice'],
    ['models', 'nav.models'],
    ['about', 'nav.about']
  ];

  const set = (key) => (value) => LL.send('setting', { key, value });

  // Menu labels follow the interface language, except the names of the languages themselves (as in the composer).
  const choices = (values, labelKeys) => values.map((value, i) => [value, LL.t(labelKeys[i])]);

  const Apps = ({ apps, appChoices }) => html`<section class="apps" aria-labelledby="apps-title">
    <div class="apps-head"><div><h2 id="apps-title">${LL.t('apps')}</h2><p class="hint">${LL.t('apps.desc')}</p></div>
      <button type="button" id="apps-clear" class="btn" disabled=${apps.length === 0} onClick=${() => LL.send('clearApps')}>${LL.t('apps.clear')}</button></div>
    ${apps.length === 0
      ? html`<p id="apps-empty" class="hint">${LL.t('apps.empty')}</p>`
      : html`<ul class="app-list">${apps.map((app) => html`<li class="app" key=${app.id}>
          <span class="app-name">${app.name}</span>
          <span class="app-summary">${app.summary}</span>
          <select class="app-select" id=${'app-mode-' + app.id} aria-label=${app.modeAria} value=${app.choice}
            onChange=${(e) => LL.send('setAppChoice', { appId: app.id, appName: app.rawName, choice: e.target.value })}>
            ${appChoices.map((opt) => html`<option key=${opt.value} value=${opt.value} selected=${opt.value === app.choice}>${opt.label}</option>`)}
          </select>
          <time>${app.used}</time>
          <button type="button" class="danger" aria-label=${LL.t('apps.forget') + ' ' + app.name}
            onClick=${() => LL.send('forgetApp', { appId: app.id })}>${LL.t('apps.forget')}</button>
        </li>`)}</ul>`}
  </section>`;

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
    <${Row} id="recent-clear" title=${LL.t('recent')} desc=${LL.t('recent.desc')}>
      <button type="button" id="recent-clear" class="btn" onClick=${() => LL.send('clearRecent')}>${LL.t('recent.clear')}</button>
    <//>
    <${Row} id="remember" title=${LL.t('remember')} desc=${LL.t('remember.desc')}>
      <${Switch} id="remember" checked=${g.rememberChoices} onChange=${(on) => set('rememberChoices')(String(on))}/>
    <//>
    <${Apps} apps=${g.apps} appChoices=${g.appChoices}/>
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

  class Corrections extends preact.Component {
    state = { spoken: '', canonical: '' };

    add = () => {
      const spoken = this.state.spoken.trim();
      const canonical = this.state.canonical.trim();
      if (!spoken || !canonical || spoken === canonical) return;
      this.setState({ spoken: '', canonical: '' });
      LL.send('addCorrection', { spoken, canonical });
    };

    onKeyDown = (e) => {
      if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) {
        e.preventDefault();
        this.add();
      }
    };

    render({ corrections }, { spoken, canonical }) {
      return html`<section class="corrections" aria-labelledby="corrections-title">
        <h2 id="corrections-title">${LL.t('corrections')}</h2>
        <div class="term-form">
          <label>${LL.t('corrections.spoken')}<input id="corr-spoken" value=${spoken} placeholder=${LL.t('corrections.spokenExample')} spellcheck="false"
            onInput=${(e) => this.setState({ spoken: e.target.value })} onKeyDown=${this.onKeyDown}/></label>
          <label>${LL.t('corrections.canonical')}<input id="corr-canonical" value=${canonical} placeholder=${LL.t('corrections.canonicalExample')} spellcheck="false"
            onInput=${(e) => this.setState({ canonical: e.target.value })} onKeyDown=${this.onKeyDown}/></label>
          <button type="button" id="corr-add" class="btn" disabled=${!spoken.trim() || !canonical.trim() || spoken.trim() === canonical.trim()} onClick=${this.add}>${LL.t('corrections.add')}</button>
        </div>
        <p class="hint">${LL.t('corrections.hint')}</p>
        <ul class="corr-list">${corrections.map((item) => html`<li class="corr-item" key=${item.i}>
          <span>${item.spoken}</span><span class="arrow" aria-hidden="true">→</span><span>${item.canonical}</span>
          <button type="button" class="danger" aria-label=${LL.t('corrections.remove') + ' ' + item.spoken}
            onClick=${() => LL.send('removeCorrection', { index: item.i })}>${LL.t('corrections.remove')}</button>
        </li>`)}</ul>
      </section>`;
    }
  }

  // The polish requirements: a text box of its own (a full-width block, not a row), saved when it loses the focus so one
  // edit is one stored value. Where nothing is customized it shows the built-in text to start from.
  class Polish extends preact.Component {
    state = { draft: null };

    commit = () => {
      const { draft } = this.state;
      this.setState({ draft: null });
      if (draft !== null && draft !== this.props.p.text) LL.send('setting', { key: 'polishPrompt', value: draft });
    };

    reset = () => {
      this.setState({ draft: null });
      LL.send('setting', { key: 'polishPrompt', value: '' });
    };

    render({ p }, { draft }) {
      return html`<section class="polish" aria-labelledby="polish-title">
        <div class="polish-head">
          <h2 id="polish-title">${LL.t('polish')}${!p.isDefault && html` <span class="chip mini">${LL.t('polish.custom')}</span>`}</h2>
          <button type="button" id="polish-reset" class="btn" disabled=${p.isDefault} onClick=${this.reset}>${LL.t('polish.reset')}</button>
        </div>
        <p class="hint">${LL.t('polish.desc')}</p>
        <textarea id="polish" rows="9" spellcheck="false" aria-labelledby="polish-title" aria-invalid=${p.invalid}
          value=${draft ?? p.text} onInput=${(e) => this.setState({ draft: e.target.value })} onBlur=${this.commit}></textarea>
        ${p.invalid ? html`<p class="err" role="alert">${LL.t('polish.invalid')}</p>` : html`<p class="hint">${LL.t('polish.save')}</p>`}
      </section>`;
    }
  }

  const PRESET_FIELD = {
    nameMissing: 'pf-name',
    nameTaken: 'pf-name',
    descInvalid: 'pf-desc',
    instructionsInvalid: 'pf-instructions'
  };

  class PresetFormModal extends preact.Component {
    constructor(props) {
      super(props);
      const f = props.form;
      this.state = {
        name: f.name,
        description: f.description,
        gate: f.gate,
        instructions: f.instructions
      };
    }

    componentDidUpdate(before) {
      const { form } = this.props;
      if (this.submitted && form !== before.form) {
        this.submitted = false;
        if (form.error) this.pane?.querySelector('#' + PRESET_FIELD[form.error])?.focus();
      }
    }

    applyStarter = (tpl) => {
      this.setState({
        name: tpl.name,
        description: tpl.description,
        gate: tpl.gate,
        instructions: tpl.instructions
      }, () => this.pane?.querySelector('#pf-instructions')?.focus());
    };

    submit = (e) => {
      e.preventDefault();
      this.submitted = true;
      const { name, description, gate, instructions } = this.state;
      LL.send('savePreset', { label: name, description, gate, instructions });
    };

    onKeyDown = (e) => {
      if (e.key === 'Enter' && e.target?.tagName !== 'TEXTAREA' && (e.isComposing || e.keyCode === 229)) {
        e.preventDefault();
      }
    };

    render({ form, starters }, { name, description, gate, instructions }) {
      const adding = form.id === null;
      const builtIn = form.builtIn;
      const bad = (field) => (form.error && PRESET_FIELD[form.error] === field ? 'true' : undefined);
      const returnTo = adding ? 'preset-add' : ('preset-edit-' + form.id);
      return html`<${Modal} id="preset-form" title=${form.title} returnTo=${returnTo}
        onClose=${() => LL.send('closePresetForm')}
        ref=${(modal) => (this.pane = modal?.pane)}>
        <form class="endpoint-form preset-form" onSubmit=${this.submit} onKeyDown=${this.onKeyDown} novalidate>
          ${adding && starters.length > 0 && html`<div class="starter-bar">
            <span class="starter-lbl">${LL.t('presets.form.starters')}</span>
            <div class="starter-chips">${starters.map((tpl) => html`<button type="button" key=${tpl.id}
              id=${'starter-' + tpl.id} class="btn compact starter-chip"
              onClick=${() => this.applyStarter(tpl)}>+ ${tpl.name}</button>`)}</div>
          </div>`}
          ${!builtIn && html`<label>${LL.t('presets.form.name')}<input id="pf-name" value=${name} maxlength="32"
            placeholder=${LL.t('presets.form.nameExample')} data-initial autocomplete="off" spellcheck="false"
            aria-invalid=${bad('pf-name')} onInput=${(e) => this.setState({ name: e.target.value })}/></label>
          <label>${LL.t('presets.form.desc')}<input id="pf-desc" value=${description} maxlength="60"
            placeholder=${LL.t('presets.form.descExample')} autocomplete="off" spellcheck="false"
            aria-invalid=${bad('pf-desc')} onInput=${(e) => this.setState({ description: e.target.value })}/></label>
          <label>${LL.t('presets.form.gate')}<select id="pf-gate" value=${gate}
            onChange=${(e) => this.setState({ gate: e.target.value })}>
            <option value="FactAnchor" selected=${gate === 'FactAnchor'}>${LL.t('presets.form.gate.factAnchor')}</option>
            <option value="Verbatim" selected=${gate === 'Verbatim'}>${LL.t('presets.form.gate.verbatim')}</option>
          </select></label>`}
          <label>${LL.t('presets.form.instructions')}<textarea id="pf-instructions" rows="7" spellcheck="false"
            data-initial=${builtIn ? true : undefined} aria-invalid=${bad('pf-instructions')}
            value=${instructions} onInput=${(e) => this.setState({ instructions: e.target.value })}></textarea></label>
          <p class="hint preset-note">${LL.t('presets.form.note')}</p>
          ${form.error && html`<p class="err" role="alert">${LL.t('presets.error.' + form.error)}</p>`}
          <div class="dlg-actions">
            <button type="button" id="pf-cancel" class="btn" onClick=${() => LL.send('closePresetForm')}>${LL.t('cloud.form.cancel')}</button>
            <button type="submit" id="pf-save" class="btn primary">${LL.t(adding ? 'cloud.form.create' : 'cloud.form.save')}</button>
          </div>
        </form>
      <//>`;
    }
  }

  const presetAnswer = (allow) => LL.send('presetAnswer', { allow });

  const DeletePresetDialog = ({ d }) => html`<${Modal} id="preset-delete" title=${fmt('presets.delete.title', d.name)}
    returnTo=${'preset-del-' + d.id} onClose=${() => presetAnswer(false)}>
    <p>${LL.t('presets.delete.body')}</p>
    <div class="dlg-actions">
      <button type="button" id="preset-keep" class="btn" data-initial autofocus onClick=${() => presetAnswer(false)}>${LL.t('cloud.delete.keep')}</button>
      <button type="button" id="preset-confirm" class="btn primary destructive" onClick=${() => presetAnswer(true)}>${LL.t('cloud.delete.confirm')}</button>
    </div>
  <//>`;

  const Presets = ({ v }) => html`<section id="presets-section" class="presets" aria-labelledby="presets-title">
    <div class="presets-head">
      <div>
        <h2 id="presets-title">${LL.t('presets')}</h2>
        <p class="hint">${LL.t('presets.desc')}</p>
      </div>
      <button type="button" id="preset-add" class="btn compact primary" onClick=${() => LL.send('openPresetForm', { id: null })}>+ ${LL.t('presets.add')}</button>
    </div>
    <ul class="preset-list">${v.presets.map((p) => html`<li id=${'preset-row-' + p.id} class=${'preset-row' + (!p.enabled ? ' disabled' : '')} key=${p.id}>
      <div class="preset-main">
        <div class="preset-title-row">
          <b class="preset-name">${p.name}</b>
          <span class=${'chip mini' + (p.builtIn ? '' : ' rec')}>${LL.t(p.builtIn ? 'presets.builtIn' : 'presets.custom')}</span>
          <span class="chip mini">${p.gateBadge}</span>
          ${p.isDefault && html`<span class="chip mini ok">${LL.t('presets.defaultBadge')}</span>`}
          ${p.builtIn && p.customized && html`<span class="chip mini warn">${LL.t('presets.tunedBadge')}</span>`}
        </div>
        ${p.description && html`<p class="preset-desc">${p.description}</p>`}
      </div>
      <div class="preset-actions">
        <button type="button" id=${'preset-edit-' + p.id} class="btn compact"
          aria-label=${LL.t(p.builtIn ? 'presets.inspect' : 'presets.edit') + ' ' + p.name}
          onClick=${() => LL.send('openPresetForm', { id: p.id })}>${LL.t(p.builtIn ? 'presets.inspect' : 'presets.edit')}</button>
        ${p.builtIn && p.customized && html`<button type="button" id=${'preset-reset-' + p.id} class="link"
          aria-label=${LL.t('presets.reset') + ' ' + p.name}
          onClick=${() => LL.send('resetPreset', { id: p.id })}>${LL.t('presets.reset')}</button>`}
        ${!p.builtIn && html`<button type="button" id=${'preset-del-' + p.id} class="link danger"
          aria-label=${LL.t('presets.delete') + ' ' + p.name}
          onClick=${() => LL.send('deletePreset', { id: p.id })}>${LL.t('presets.delete')}</button>`}
        <input type="checkbox" role="switch" id=${'preset-on-' + p.id} aria-label=${p.toggleAria}
          checked=${p.enabled} disabled=${!p.canToggle}
          onChange=${(e) => {
            e.target.checked = p.enabled;
            LL.send('togglePreset', { id: p.id, enabled: !p.enabled });
          }}/>
      </div>
    </li>`)}</ul>
  </section>`;

  const Voice = ({ v }) => {
    const enabledPresetOpts = v.presets.filter((p) => p.enabled).map((p) => [p.id, p.name]);
    return html`<div>
      <${Row} id="mic" title=${LL.t('mic')} desc=${LL.t('mic.desc')}>
        <${Select} id="mic" value=${v.microphone} options=${v.microphones.map((name) => [name, name || LL.t('mic.default')])} onChange=${set('microphone')}/>
      <//>
      <${Row} id="cleanup" title=${LL.t('cleanup')} desc=${LL.t('cleanup.desc')}>
        <${Switch} id="cleanup" checked=${v.cleanup} onChange=${(on) => set('cleanup')(String(on))}/>
      <//>
      <${Row} id="direction" title=${LL.t('direction')} desc=${LL.t('direction.desc')}>
        <${Select} id="direction" value=${v.direction} options=${choices(['English', 'Chinese'], ['direction.zhEn', 'direction.enZh'])} onChange=${set('direction')}/>
      <//>
      <${Row} id="output" title=${LL.t('output')} desc=${LL.t('output.desc')}>
        <${Select} id="output" value=${v.defaultOutput} options=${choices(['Clean', 'Translate', 'Original'], ['output.clean', 'output.translate', 'output.original'])} onChange=${set('defaultOutput')}/>
      <//>
      <${Row} id="default-polish" title=${LL.t('defaultPolish')} desc=${LL.t('defaultPolish.desc')}>
        <${Select} id="default-polish" value=${v.defaultPolishMode} options=${enabledPresetOpts} onChange=${set('defaultPolishMode')}/>
      <//>
      <${Row} id="smart-host-polish" title=${LL.t('smartHostPolish')} desc=${LL.t('smartHostPolish.desc')}>
        <${Switch} id="smart-host-polish" checked=${v.smartHostPolish} onChange=${(on) => set('smartHostPolish')(String(on))}/>
      <//>
      <${Row} id="auto-dictate" title=${LL.t('autoDictate')} desc=${LL.t('autoDictate.desc')}>
        <${Switch} id="auto-dictate" checked=${v.autoDictate} onChange=${(on) => set('autoDictate')(String(on))}/>
      <//>
      <${Row} id="preload" title=${LL.t('preload')} desc=${LL.t('preload.desc')}>
        <${Switch} id="preload" checked=${v.preloadSpeech} onChange=${(on) => set('preloadSpeech')(String(on))}/>
      <//>
      <${Row} id="builtin-terms" title=${LL.t('builtInTerms')} desc=${LL.t('builtInTerms.desc')}>
        <${Switch} id="builtin-terms" checked=${v.builtInGlossary} onChange=${(on) => set('builtInGlossary')(String(on))}/>
      <//>
      <${Presets} v=${v}/>
      <${Polish} p=${{ text: v.polishPrompt, isDefault: v.polishPromptIsDefault, invalid: v.polishPromptInvalid }}/>
      <${Corrections} corrections=${v.corrections}/>
      <${Terms} terms=${v.terms}/>
      ${v.presetForm && html`<${PresetFormModal} key=${v.presetForm.id ?? 'add'} form=${v.presetForm} starters=${v.starterTemplates}/>`}
      ${v.deletePreset && html`<${DeletePresetDialog} d=${v.deletePreset}/>`}
    </div>`;
  };

  // The page only has the strings; the placeholders ({0}, {1}…) are filled here.
  const fmt = (key, ...args) => args.reduce((text, arg, i) => text.split('{' + i + '}').join(arg), LL.t(key));

  const FORM_FIELD = { nameMissing: 'ep-name', nameTaken: 'ep-name', urlInvalid: 'ep-url', modelMissing: 'ep-model', keyMissing: 'ep-key' };

  // A modal dialog: keeps the focus inside, closes on Escape and gives the focus back to what opened it.
  // The child marked data-initial takes the focus first; it is the safe answer.
  class Modal extends preact.Component {
    componentDidMount() {
      this.opener = document.activeElement;
      this.pane.querySelector('[data-initial]').focus();
      document.addEventListener('keydown', this.onKeyDown);
    }

    componentWillUnmount() {
      document.removeEventListener('keydown', this.onKeyDown);
      const explicit = this.props.returnTo ? document.getElementById(this.props.returnTo) : null;
      const fallback = this.opener && this.opener.isConnected && !this.pane?.contains(this.opener) ? this.opener : null;
      (explicit || fallback)?.focus();
    }

    onKeyDown = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        this.props.onClose();
      } else if (e.key === 'Tab') {
        const items = [...this.pane.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)')];
        const at = items.indexOf(document.activeElement);
        const next = (at + (e.shiftKey ? -1 : 1) + items.length) % items.length;
        e.preventDefault();
        items[next].focus();
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

  // The check and download report here; the progress bar is the download's, and a check without a known length
  // (looking at the feed) only shows that it is busy.
  const Update = ({ u }) => html`<${Row} id="update-action" title=${LL.t('update')} desc=${LL.t('update.desc')}
    extra=${html`<p id="update-status" role="status" aria-live="polite">${u.status}</p>
      ${u.busy && html`<div id="update-progress" class=${'progress' + (u.percent === null ? ' indeterminate' : '')} role="progressbar"
        aria-valuemin="0" aria-valuemax="100" aria-valuenow=${u.percent === null ? undefined : u.percent}>
        <span style=${u.percent === null ? undefined : { width: u.percent + '%' }}></span></div>`}`}>
    ${(u.canCheck || u.busy) && html`<button type="button" id="update-action" class="btn" disabled=${u.busy} onClick=${() => LL.send('checkUpdates')}>${LL.t('update.check')}</button>`}
    ${u.canRestart && html`<button type="button" id="update-action" class="btn primary" onClick=${() => LL.send('restartToUpdate')}>${LL.t('update.restart')}</button>`}
    ${u.canDownload && html`<button type="button" id="update-action" class="btn primary" onClick=${() => LL.send('openDownloads')}>${LL.t('update.download')}</button>`}
  <//>`;

  const Stars = ({ label, count }) => {
    const filled = '●'.repeat(count) + '○'.repeat(Math.max(0, 3 - count));
    return html`<span class="mmetric" aria-label=${label + ' ' + count + '/3'}>${label} <span class="mdots" aria-hidden="true">${filled}</span></span>`;
  };

  const fitChipClass = (fit) =>
    fit === 'recommended' ? 'chip mini rec'
    : fit === 'heavy' ? 'chip mini warn'
    : fit === 'unsupported' ? 'chip mini off'
    : 'chip mini';

  const ModelCard = ({ stage, opt }) => {
    const radioId = 'model-radio-' + opt.id;
    const choose = () => {
      if (opt.canSelectImmediate) LL.send('selectModel', { modelId: opt.id });
    };
    return html`<li id=${'model-card-' + opt.id}
      class=${'mcard' + (opt.active ? ' active' : '') + (opt.fit === 'unsupported' ? ' unsupported' : '') + (opt.canSelectImmediate ? ' selectable' : '')}
      onClick=${(e) => {
        if (!e.target.closest('button, input, label')) choose();
      }}>
      <div class="mcard-top">
        <input type="radio" class="mradio" id=${radioId} name=${'stage-' + stage}
          aria-label=${opt.name} checked=${opt.active} disabled=${!opt.canSelectImmediate && !opt.active}
          onChange=${(e) => {
            e.target.checked = opt.active;
            choose();
          }}/>
        <div class="mcard-body">
          <div class="mcard-head">
            <span class="mrank">${'#' + opt.rank}</span>
            <label class="mname" for=${radioId}>${opt.name}</label>
            <span class=${fitChipClass(opt.fit)}>${opt.fitBadge}</span>
            ${opt.active && opt.installed && html`<span class="chip mini ok">${LL.t('models.state.activeInstalled')}</span>`}
            ${opt.active && !opt.installed && html`<span class="chip mini ok">${LL.t('models.state.selected')}</span>`}
            ${!opt.active && opt.pendingActivation && opt.downloading && html`<span class="chip mini rec">${LL.t('models.state.activating')}</span>`}
            ${!opt.active && opt.installed && html`<span class="chip mini">${LL.t('models.state.installed')}</span>`}
          </div>
          <p class="mcard-desc">
            <${Stars} label=${LL.t('models.metric.quality')} count=${opt.quality}/>
            <${Stars} label=${LL.t('models.metric.speed')} count=${opt.speed}/>
            <span>${opt.summary}</span>
          </p>
          <p class="mcard-foot">
            <span>${opt.footprint}</span>
            ${opt.fitDetail && html`<span class=${'mfit-note ' + opt.fit}> · ${opt.fitDetail}</span>`}
          </p>
        </div>
        <div class="mcard-actions">
          ${opt.downloading && html`<button type="button" id=${'model-cancel-' + opt.id} class="btn compact"
            aria-label=${LL.t('models.action.cancel') + ' ' + opt.name}
            onClick=${() => LL.send('cancelModelDownload', { modelId: opt.id })}>${LL.t('models.action.cancel')}</button>`}
          ${opt.canDownload && html`<button type="button" id=${'model-download-' + opt.id}
            class=${'btn compact' + (opt.active ? ' primary' : '')}
            aria-label=${opt.downloadActionLabel + ' ' + opt.name}
            onClick=${() => LL.send('downloadModel', { modelId: opt.id })}>${opt.downloadActionLabel}</button>`}
          ${opt.canSelectImmediate && opt.installed && html`<button type="button" id=${'model-use-' + opt.id} class="btn compact"
            aria-label=${LL.t('models.action.enable') + ' ' + opt.name}
            onClick=${choose}>${LL.t('models.action.enable')}</button>`}
          ${opt.canDelete && html`<button type="button" id=${'model-delete-' + opt.id} class="link danger"
            aria-label=${(opt.deleteActionLabel || LL.t('models.action.delete')) + ' ' + opt.name}
            onClick=${() => LL.send('deleteModel', { modelId: opt.id })}>${opt.deleteActionLabel || LL.t('models.action.delete')}</button>`}
        </div>
      </div>
      ${opt.downloading && html`<div class="mcard-progress">
        <div class=${'progress' + (opt.downloadPercent === null ? ' indeterminate' : '')} role="progressbar" aria-label=${opt.name}
          aria-valuemin="0" aria-valuemax="100" aria-valuenow=${opt.downloadPercent === null ? undefined : opt.downloadPercent}>
          <span style=${opt.downloadPercent === null ? undefined : { width: opt.downloadPercent + '%' }}></span>
        </div>
        <span class="mprog-text" role="status" aria-live="polite">${opt.downloadText}</span>
      </div>`}
      ${opt.error && html`<p class="err mcard-err" role="alert">${opt.error}</p>`}
    </li>`;
  };

  const CloudModelCard = ({ stage, step, ep }) => {
    const radioId = 'model-cloud-' + step + '-' + ep.id;
    const canChoose = ep.usable && !ep.active;
    const choose = () => {
      if (canChoose) LL.send('selectCloudModel', { step, endpointId: ep.id });
    };
    return html`<li id=${'model-cloud-card-' + step + '-' + ep.id}
      class=${'mcard cloud' + (ep.active ? ' active' : '') + (!ep.usable && !ep.active ? ' unsupported' : '') + (canChoose ? ' selectable' : '')}
      onClick=${(e) => {
        if (!e.target.closest('button, input, label')) choose();
      }}>
      <div class="mcard-top">
        <input type="radio" class="mradio" id=${radioId} name=${'stage-' + stage}
          aria-label=${ep.name + ' · ' + ep.model} checked=${ep.active} disabled=${!canChoose && !ep.active}
          onChange=${(e) => {
            e.target.checked = ep.active;
            choose();
          }}/>
        <div class="mcard-body">
          <div class="mcard-head">
            <span class="mrank cloud">${LL.t('models.cloud.badge')}</span>
            <label class="mname" for=${radioId}>${ep.name}</label>
            <span class="chip mini">${ep.model}</span>
            ${ep.active && html`<span class="chip mini ok">${LL.t('models.state.activeInstalled')}</span>`}
            ${ep.peerBadge && html`<span class="chip mini">${ep.peerBadge}</span>`}
            ${!ep.usable && html`<span class="chip mini warn">${LL.t('models.cloud.incomplete')}</span>`}
          </div>
          <p class="mcard-desc">
            <span>${ep.host} · ${ep.hasKey ? LL.t('cloud.keySaved') : LL.t('cloud.keyMissing')}</span>
          </p>
          <p class="mcard-foot">
            <span>${LL.t(stage === 'speech' ? 'models.cloud.speechFootprint' : 'models.cloud.footprint')}</span>
          </p>
        </div>
        <div class="mcard-actions">
          ${ep.active && html`<button type="button" id=${'model-cloud-local-' + step + '-' + ep.id} class="btn compact"
            aria-label=${LL.t('models.cloud.useLocal') + ' ' + ep.name}
            onClick=${() => LL.send('useLocalStage', { step })}>${LL.t('models.cloud.useLocal')}</button>`}
          ${canChoose && html`<button type="button" id=${'model-cloud-use-' + step + '-' + ep.id} class="btn compact"
            aria-label=${LL.t('models.action.enable') + ' ' + ep.name}
            onClick=${choose}>${LL.t('models.action.enable')}</button>`}
          <div class="mcard-links">
            <button type="button" id=${'stage-cloud-edit-' + stage + '-' + ep.id} class="link"
              aria-label=${LL.t('cloud.edit') + ' ' + ep.name}
              onClick=${() => LL.send('openEndpointForm', { id: ep.id })}>${LL.t('cloud.edit')}</button>
            <button type="button" id=${'stage-cloud-delete-' + stage + '-' + ep.id} class="link danger"
              aria-label=${LL.t('cloud.delete') + ' ' + ep.name}
              onClick=${() => LL.send('deleteEndpoint', { id: ep.id })}>${LL.t('cloud.delete')}</button>
          </div>
        </div>
      </div>
    </li>`;
  };

  const ModelStageSection = ({ group }) => html`<section id=${'stage-' + group.stage} class="mstage"
    role="tabpanel" aria-labelledby=${'model-tab-' + group.stage}>
    <div class="mstage-head">
      <h2 id=${'stage-title-' + group.stage}>${group.title}</h2>
      <p class="hint">${group.desc}</p>
    </div>
    ${group.localOnly && html`<div id="speech-local-only" class="mstage-local-pill" role="note">
      <span class="chip mini ok">${LL.t('privacy.badge.alwaysLocal')}</span>
      <span>${LL.t('models.speech.localOnly')}</span>
    </div>`}
    ${group.notice && html`<div class="mstage-notice" role="status">
      <span>${group.notice}</span>
    </div>`}
    ${group.cloudStep && html`<div class="msource-group">
      <div class="msource-head">
        <h3 class="msource-title">${LL.t(group.stage === 'speech' ? 'models.group.cloud.speechTitle' : 'models.group.cloud.title')}</h3>
        <div class="msource-links">
          <button type="button" id=${'stage-cloud-add-' + group.stage} class="link"
            onClick=${() => LL.send('openEndpointForm', { id: null })}>+ ${LL.t('models.cloud.add')}</button>
        </div>
      </div>
      ${group.cloudInvalid && html`<p class="err msource-err" role="alert">${LL.t('cloud.invalid')}</p>`}
      ${group.cloudOptions.length === 0
        ? html`<div id=${'stage-cloud-empty-' + group.stage} class="mcloud-empty">
            <span>${LL.t(group.stage === 'speech' ? 'models.cloud.speechEmpty' : 'models.cloud.empty')}</span>
            <button type="button" id=${'stage-cloud-empty-add-' + group.stage} class="btn compact"
              onClick=${() => LL.send('openEndpointForm', { id: null })}>${LL.t('models.cloud.add')}</button>
          </div>`
        : html`<ul class="mcard-list">${group.cloudOptions.map((ep) =>
            html`<${CloudModelCard} key=${ep.id} stage=${group.stage} step=${group.cloudStep} ep=${ep}/>`)}</ul>`}
    </div>`}
    ${group.cloudStep && html`<div class="msource-head local">
      <h3 class="msource-title">${LL.t('models.group.local.title')}</h3>
    </div>`}
    <ul class="mcard-list">${group.options.map((opt) => html`<${ModelCard} key=${opt.id} stage=${group.stage} opt=${opt}/>`)}</ul>
  </section>`;

  const DownloadCenter = ({ downloads }) => {
    if (!downloads || downloads.length === 0) return null;
    return html`<section id="models-downloads" class="mdl-center" aria-labelledby="models-dl-title">
      <div class="mdl-head">
        <h2 id="models-dl-title">${LL.t('models.dl.title')}</h2>
        <span class="chip mini rec">${downloads.length}</span>
      </div>
      <ul class="mdl-list">${downloads.map((d) => html`<li id=${'dl-item-' + d.id} class=${'mdl-item' + (d.failed ? ' failed' : '')} key=${d.id}>
        <div class="mdl-row">
          <div class="mdl-title">
            <span class="chip mini">${d.category}</span>
            <b>${d.name}</b>
          </div>
          <div class="mdl-right">
            <span class=${'mdl-status' + (d.failed ? ' err' : '')} role=${d.failed ? 'alert' : 'status'} aria-live="polite">${d.statusText}</span>
            ${d.canCancel && html`<button type="button" id=${'dl-cancel-' + d.id} class="btn compact"
              aria-label=${LL.t('models.action.cancel') + ' ' + d.name}
              onClick=${() => LL.send('cancelModelDownload', { modelId: d.id })}>${LL.t('models.action.cancel')}</button>`}
            ${d.canRetry && html`<button type="button" id=${'dl-retry-' + d.id} class="btn compact primary"
              aria-label=${LL.t('models.action.retry') + ' ' + d.name}
              onClick=${() => LL.send('downloadModel', { modelId: d.id })}>${LL.t('models.action.retry')}</button>`}
          </div>
        </div>
        ${!d.failed && html`<div class=${'progress' + (d.percent === null ? ' indeterminate' : '')} role="progressbar"
          aria-label=${d.name} aria-valuemin="0" aria-valuemax="100" aria-valuenow=${d.percent === null ? undefined : d.percent}>
          <span style=${d.percent === null ? undefined : { width: d.percent + '%' }}></span>
        </div>`}
      </li>`)}</ul>
    </section>`;
  };

  const ModelStorageSection = ({ s }) => html`<section id="models-storage" class="mstorage"
    role="tabpanel" aria-labelledby="model-tab-storage">
    <div class="mstage-head">
      <h2 id="storage-title">${LL.t('models.storage.title')}</h2>
      <p class="hint">${LL.t('models.storage.desc')}</p>
    </div>
    <div class="storage-summary">
      <div class="storage-stat">
        <span class="storage-kicker">${LL.t('models.storage.total')}</span>
        <strong id="storage-total" class="storage-total">${s.totalSize}</strong>
        <p class="storage-breakdown">${s.breakdown}</p>
      </div>
      ${s.path && html`<div class="storage-path">
        <span class="storage-kicker">${LL.t('models.storage.path')}</span>
        <code id="storage-path">${s.path}</code>
      </div>`}
    </div>
    ${s.legacy.count > 0 && html`<div class="storage-legacy">
      <${Row} id="legacy-remove" title=${LL.t('legacy')} desc=${s.legacy.text}>
        <button type="button" id="legacy-remove" class="btn compact" onClick=${() => LL.send('removeLegacyModels')}>${LL.t('legacy.remove')}</button>
      <//>
    </div>`}
    <div class="storage-inventory">
      <h3 class="storage-subhead">${LL.t('models.storage.listTitle')}</h3>
      ${s.items.length === 0
        ? html`<p id="storage-empty" class="hint storage-empty">${LL.t('models.storage.empty')}</p>`
        : html`<ul class="storage-list">${s.items.map((item) => html`<li id=${'storage-item-' + item.id} class="storage-item" key=${item.id}>
            <div class="storage-main">
              <div class="storage-item-head">
                <span class="chip mini">${item.category}</span>
                <b>${item.name}</b>
                <span class=${'chip mini' + (item.inUse ? ' ok' : '')}>${item.statusBadge}</span>
              </div>
            </div>
            <div class="storage-meta">
              <span class="storage-size">${item.sizeText}</span>
              ${item.canActivate && html`<button type="button" id=${'storage-use-' + item.id} class="btn compact"
                aria-label=${LL.t('models.action.enable') + ' ' + item.name}
                onClick=${() => LL.send('selectModel', { modelId: item.id })}>${LL.t('models.action.enable')}</button>`}
              ${item.canDelete && html`<button type="button" id=${'storage-delete-' + item.id} class="link danger"
                aria-label=${(item.deleteActionLabel || LL.t('models.action.delete')) + ' ' + item.name}
                onClick=${() => LL.send('deleteModel', { modelId: item.id })}>${item.deleteActionLabel || LL.t('models.action.delete')}</button>`}
            </div>
          </li>`)}</ul>`}
    </div>
  </section>`;

  const HardwareBanner = ({ hw, onOpenStorage }) => html`<section id="models-hw" class="mhw" aria-labelledby="models-hw-title">
    <div class="mhw-top">
      <div class="mhw-info">
        <h2 id="models-hw-title">${LL.t('models.hw.title')}</h2>
        <p id="models-hw-summary" class="mhw-summary">${hw.summary}</p>
      </div>
      <div class="mhw-cta">
        ${hw.isRecommended
          ? html`<span id="models-best-badge" class="chip rec">${LL.t('models.hw.matched')}</span>`
          : hw.isRecommendedDownloading
          ? html`<span id="models-best-preparing" class="chip rec" role="status">${LL.t('models.hw.preparing')}</span>`
          : html`<button type="button" id="models-apply-best" class="btn primary compact"
              onClick=${() => LL.send('applyRecommendedModels')}>${hw.recommendActionLabel}</button>`}
      </div>
    </div>
    <div class="mhw-meter">
      <div class=${'progress' + (hw.overBudget ? ' warn' : '')} role="progressbar" aria-label=${LL.t('models.hw.gaugeAria')}
        aria-valuemin="0" aria-valuemax="100" aria-valuenow=${hw.budgetPercent}>
        <span style=${{ width: hw.budgetPercent + '%' }}></span>
      </div>
      <div class="mhw-meta-row">
        <p class="mhw-meta"><span>${hw.residency}</span></p>
        <button type="button" id="models-disk-link" class="link mhw-disk-btn" onClick=${onOpenStorage}>${hw.diskUsed}</button>
      </div>
      <p class=${'mhw-note' + (hw.overBudget ? ' warn' : '')}>${hw.modeNote}</p>
    </div>
    <div id="models-privacy-strip" class="mhw-privacy">
      <div class="mhw-privacy-status">
        <span class=${'chip mini ' + (hw.anyCloud ? 'warn' : 'ok')}>${LL.t(hw.anyCloud ? 'privacy.badge.cloud' : 'privacy.badge.local')}</span>
        <span>${hw.privacyStatus}</span>
      </div>
      ${hw.anyCloud && html`<button type="button" id="cloud-disable-all" class="btn compact"
        onClick=${() => LL.send('disableAllCloud')}>${LL.t('privacy.routing.allLocal')}</button>`}
    </div>
  </section>`;

  // Selecting a cloud endpoint for any stage requires explicit user consent; the safe answer is always focused first.
  const answer = (allow) => LL.send('consent', { allow });

  const Consent = ({ c }) => html`<${Modal} id="consent" title=${LL.t('consent.' + c.step)}
    returnTo=${'model-cloud-' + c.step + '-' + c.endpointId} onClose=${() => answer(false)}>
    <p>${c.step === 'speech'
      ? (c.previousEndpointName
          ? fmt('consent.speechSwitchBody', c.previousEndpointName, c.endpointName, c.host)
          : fmt('consent.speechBody', c.endpointName, c.host))
      : (c.previousEndpointName
          ? fmt('consent.switchBody', c.previousEndpointName, c.endpointName, c.host)
          : fmt('consent.body', c.endpointName, c.host))}</p>
    <div class="dlg-actions">
      <button type="button" id="consent-keep" class="btn" data-initial onClick=${() => answer(false)}>${LL.t(c.previousEndpointName ? 'consent.keepCurrent' : 'consent.keepLocal')}</button>
      <button type="button" id="consent-allow" class="btn primary" onClick=${() => answer(true)}>${LL.t('consent.allow')}</button>
    </div>
  <//>`;

  // The add or edit form. The fields are the page's until they are sent; the key stays in this form only while it is open
  // (it is kept across a refused submission so a wrong name does not mean typing it again) and never goes into a snapshot.
  class EndpointForm extends preact.Component {
    constructor(props) {
      super(props);
      const e = props.endpoint;
      this.state = { name: e?.name ?? '', url: e?.url ?? '', model: e?.model ?? '', key: '' };
    }

    componentDidUpdate(before) {
      const { form } = this.props;
      if (this.submitted && form !== before.form) {
        this.submitted = false;
        if (form.error) this.pane.querySelector('#' + FORM_FIELD[form.error])?.focus();
      }
    }

    field = (name) => (e) => this.setState({ [name]: e.target.value });

    fillQwenAsr = () => {
      this.setState({
        name: 'Qwen-Audio-3.1-ASR',
        url: 'https://dashscope.aliyuncs.com/api/v1',
        model: 'qwen-audio-3.1-asr-flash-message'
      }, () => this.pane?.querySelector('#ep-key')?.focus());
    };

    submit = (e) => {
      e.preventDefault();
      const { name, url, model, key } = this.state;
      this.submitted = true;
      LL.send('saveEndpoint', { label: name, baseUrl: url, model, apiKey: key });
    };

    // An input method confirms its candidate with Enter (keyCode 229 in WKWebView); that is not "submit".
    onKeyDown = (e) => {
      if (e.key === 'Enter' && (e.isComposing || e.keyCode === 229)) e.preventDefault();
    };

    render({ form, endpoint, stage }, { name, url, model, key }) {
      const adding = form.id === null;
      const isSpeech = stage === 'speech';
      const bad = (field) => (form.error && FORM_FIELD[form.error] === field ? 'true' : undefined);
      const returnTo = adding ? ('stage-cloud-add-' + stage) : ('stage-cloud-edit-' + stage + '-' + form.id);
      return html`<${Modal} id="endpoint-form" title=${LL.t(adding ? 'cloud.form.add' : 'cloud.form.edit')}
        returnTo=${returnTo} onClose=${() => LL.send('closeEndpointForm')}
        ref=${(modal) => (this.pane = modal?.pane)}>
        <form class="endpoint-form" onSubmit=${this.submit} onKeyDown=${this.onKeyDown} novalidate>
          ${adding && isSpeech && html`<div class="ep-preset">
            <span>${LL.t('cloud.preset.qwenAsrHint')}</span>
            <button type="button" id="ep-preset-qwen-asr" class="btn compact" onClick=${this.fillQwenAsr}>${LL.t('cloud.preset.qwenAsr')}</button>
          </div>`}
          <label>${LL.t('cloud.name')}<input id="ep-name" value=${name} maxlength="40"
            placeholder=${LL.t(isSpeech ? 'cloud.speech.name.example' : 'cloud.name.example')} data-initial
            autocomplete="off" spellcheck="false" aria-invalid=${bad('ep-name')} onInput=${this.field('name')}/></label>
          <label>${LL.t('cloud.baseUrl')}<input id="ep-url" type="url" value=${url}
            placeholder=${isSpeech ? 'https://dashscope.aliyuncs.com/api/v1' : 'https://…/v1'}
            autocomplete="off" spellcheck="false" aria-invalid=${bad('ep-url')} onInput=${this.field('url')}/>
            <small>${LL.t(isSpeech ? 'cloud.speech.baseUrl.desc' : 'cloud.baseUrl.desc')}</small></label>
          <label>${LL.t('cloud.model')}<input id="ep-model" value=${model}
            placeholder=${isSpeech ? 'qwen-audio-3.1-asr-flash-message' : ''}
            autocomplete="off" spellcheck="false" aria-invalid=${bad('ep-model')} onInput=${this.field('model')}/>
            <small>${LL.t(isSpeech ? 'cloud.speech.model.desc' : 'cloud.model.desc')}</small></label>
          <label>${LL.t('cloud.apiKey')}<input id="ep-key" type="password" value=${key} placeholder=${endpoint?.hasKey ? LL.t('cloud.keyStored') : ''}
            autocomplete="off" spellcheck="false" aria-invalid=${bad('ep-key')} onInput=${this.field('key')}/>
            <small>${LL.t('cloud.apiKey.desc')}</small></label>
          ${form.error && html`<p class="err" role="alert">${LL.t('cloud.error.' + form.error)}</p>`}
          <div class="dlg-actions">
            <button type="button" id="ep-cancel" class="btn" onClick=${() => LL.send('closeEndpointForm')}>${LL.t('cloud.form.cancel')}</button>
            <button type="submit" id="ep-save" class="btn primary">${LL.t(adding ? 'cloud.form.create' : 'cloud.form.save')}</button>
          </div>
        </form>
      <//>`;
    }
  }

  // Deleting an endpoint asks first and explains if any active cloud step will return to local models.
  const endpointAnswer = (allow) => LL.send('endpointAnswer', { allow });

  const DeleteEndpointDialog = ({ d, stage }) => html`<${Modal} id="endpoint-change" title=${fmt('cloud.delete.title', d.name)}
    returnTo=${'stage-cloud-delete-' + stage + '-' + d.id} onClose=${() => endpointAnswer(false)}>
    <p>${LL.t('cloud.delete.body') + (d.affectedSteps ? ' ' + fmt('cloud.delete.steps', d.affectedSteps) : '')}</p>
    <div class="dlg-actions">
      <button type="button" id="endpoint-keep" class="btn" data-initial onClick=${() => endpointAnswer(false)}>${LL.t('cloud.delete.keep')}</button>
      <button type="button" id="endpoint-confirm" class="btn primary destructive" onClick=${() => endpointAnswer(true)}>${LL.t('cloud.delete.confirm')}</button>
    </div>
  <//>`;

  class Models extends preact.Component {
    constructor(props) {
      super(props);
      this.state = { tab: props.m.tab || 'speech' };
    }

    componentDidUpdate(before) {
      if (this.props.m.tab && this.props.m.tab !== before.m.tab && this.props.m.tab !== this.state.tab) {
        this.setState({ tab: this.props.m.tab });
      }
    }

    selectTab = (tab) => this.setState({ tab });

    onTabKeyDown = (tabs) => (e) => {
      const idx = tabs.findIndex((t) => t.id === this.state.tab);
      let next = -1;
      if (e.key === 'ArrowRight') next = (idx + 1) % tabs.length;
      else if (e.key === 'ArrowLeft') next = (idx - 1 + tabs.length) % tabs.length;
      else if (e.key === 'Home') next = 0;
      else if (e.key === 'End') next = tabs.length - 1;
      if (next >= 0) {
        e.preventDefault();
        const targetId = tabs[next].id;
        this.setState({ tab: targetId }, () => document.getElementById('model-tab-' + targetId)?.focus());
      }
    };

    render({ m }, { tab }) {
      const tabs = [
        ...m.stages.map((g) => ({
          id: g.stage,
          title: g.tabTitle,
          summary: g.tabSummary,
          busy: g.busy,
          panelId: 'stage-' + g.stage
        })),
        {
          id: 'storage',
          title: m.storage.tabTitle,
          summary: m.storage.tabSummary,
          busy: false,
          panelId: 'models-storage'
        }
      ];
      const activeStage = m.stages.find((g) => g.stage === tab);
      const cloudStage = (tab === 'speech' || tab === 'cleaner' || tab === 'translator') ? tab : 'speech';
      const cloudEndpoints = m.stages.find((g) => g.stage === cloudStage)?.cloudOptions
        ?? m.stages.find((g) => g.cloudStep)?.cloudOptions
        ?? [];

      return html`<div class="models-page">
        <${HardwareBanner} hw=${m.hardware} onOpenStorage=${() => this.selectTab('storage')}/>
        <${DownloadCenter} downloads=${m.downloads}/>
        <div id="models-tabs" class="mtabs" role="tablist" aria-label=${LL.t('models.tabs.aria')}
          onKeyDown=${this.onTabKeyDown(tabs)}>
          ${tabs.map((t) => html`<button type="button" role="tab" key=${t.id} id=${'model-tab-' + t.id}
            class=${'mtab' + (tab === t.id ? ' on' : '') + (t.busy ? ' busy' : '')}
            aria-selected=${String(tab === t.id)} aria-controls=${t.panelId}
            tabIndex=${tab === t.id ? 0 : -1}
            onClick=${() => this.selectTab(t.id)}>
            <span class="mtab-title">${t.busy && html`<i class="dot" aria-hidden="true"></i>`}${t.title}</span>
            <span class="mtab-sub">${t.summary}</span>
          </button>`)}
        </div>
        ${activeStage ? html`<${ModelStageSection} key=${activeStage.stage} group=${activeStage}/>`
          : html`<${ModelStorageSection} s=${m.storage}/>`}
        ${m.consent && html`<${Consent} c=${m.consent}/>`}
        ${m.form && html`<${EndpointForm} form=${m.form} stage=${cloudStage} endpoint=${cloudEndpoints.find((e) => e.id === m.form.id)}/>`}
        ${m.deleteConfirm && html`<${DeleteEndpointDialog} d=${m.deleteConfirm} stage=${cloudStage}/>`}
      </div>`;
    }
  }

  const About = ({ a }) => html`<div class="about-page">
    <section class="about-hero" aria-labelledby="about-app-name">
      <div>
        <h2 id="about-app-name" class="about-title">${LL.t('appName')}</h2>
        <p class="hint">${LL.t('about.subtitle')}</p>
      </div>
      <span id="version" class="chip rec">${a.version}</span>
    </section>
    <${Update} u=${a.update}/>
    <${Diagnostics} lines=${a.diagnostics}/>
  </div>`;

  LL.Settings = () => {
    const s = LL.state.settings;
    if (!s) return null;
    return html`<div class="settings">
      <nav aria-label=${LL.t('nav.aria')}>${SECTIONS.map(([id, key]) => html`<button type="button" key=${id} id=${'nav-' + id}
        class=${id === 'about' ? 'nav-about' : undefined}
        aria-current=${s.section === id ? 'page' : undefined} onClick=${() => LL.send('openSettings', { section: id })}>
        <span>${LL.t(key)}</span>
        ${id === 'models' && s.models.navBadge && html`<span id="nav-models-badge" class="nav-badge">${s.models.navBadge}</span>`}
      </button>`)}</nav>
      <main id="panel"><div class="panel-body">
        ${s.section === 'general' && html`<${General} g=${s.general}/>`}
        ${s.section === 'voice' && html`<${Voice} v=${s.voice}/>`}
        ${s.section === 'models' && html`<${Models} m=${s.models}/>`}
        ${s.section === 'about' && html`<${About} a=${s.about}/>`}
      </div></main>
    </div>`;
  };
})();
