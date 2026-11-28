// The settings page: six focused domains grouped into three sidebar sections, every change applied at once.
// The page only draws the snapshot and reports intents; C# validates and stores.
(function () {
  'use strict';

  const html = LL.html;
  const { Row, Group, SegRadio, Select, Switch, HotkeyField, Icon } = LL;

  const NAV_GROUPS = [
    {
      key: 'nav.group.basic',
      items: [
        ['general', 'nav.general', 'sliders'],
        ['voice', 'nav.voice', 'mic']
      ]
    },
    {
      key: 'nav.group.quality',
      items: [
        ['polish', 'nav.polish', 'sparkle'],
        ['glossary', 'nav.glossary', 'book']
      ]
    },
    {
      key: 'nav.group.system',
      items: [
        ['models', 'nav.models', 'cpu']
      ]
    }
  ];

  const BUILTIN_DEV_TERMS = [
    'Agent', 'Skill', 'Prompt', 'API', 'PR', 'VS Code', 'Cursor', 'Claude Code',
    'MCP', 'Docker', 'Kubernetes', 'Redis', 'TypeScript', 'CI/CD'
  ];

  const PRESET_ICON = {
    'ai-task': 'terminal',
    natural: 'chat',
    formal: 'briefcase',
    todo: 'list-check',
    report: 'file-text'
  };

  const set = (key) => (value) => LL.send('setting', { key, value });
  const choices = (values, labelKeys) => values.map((value, i) => [value, LL.t(labelKeys[i])]);
  const fmt = (key, ...args) => args.reduce((text, arg, i) => text.split('{' + i + '}').join(arg), LL.t(key));

  const PageHead = ({ title, desc, action }) => html`<header class="spage-head">
    <div><h2>${title}</h2><p>${desc}</p></div>
    ${action && html`<div class="spage-head-actions">${action}</div>`}
  </header>`;

  // ==================== 1. GENERAL ====================
  const ThemeCards = ({ value, onChange }) => {
    const items = [
      ['Auto', LL.t('theme.auto'), 'tp-auto'],
      ['Light', LL.t('theme.light'), 'tp-light'],
      ['Dark', LL.t('theme.dark'), 'tp-dark']
    ];
    return html`<div id="theme" class="theme-cards" role="radiogroup" aria-label=${LL.t('theme')}>
      ${items.map(([v, label, previewCls]) => html`<label key=${v} class=${'choice-card' + (v === value ? ' on' : '')}>
        <input type="radio" name="theme" value=${v} checked=${v === value}
          onChange=${(e) => {
            e.target.checked = v === value;
            if (v !== value) onChange(v);
          }}/>
        <span class=${'theme-preview ' + previewCls} aria-hidden="true">
          ${v === 'Auto'
            ? html`<span class="tp-half-l"><i></i><i></i></span><span class="tp-half-r"><i></i><i></i></span>`
            : html`<span class="tp-side"></span><span class="tp-main"><i class="tp-bar accent"></i><i class="tp-bar"></i></span>`}
        </span>
        <span class="choice-card-top">
          <span class="choice-card-title">${label}</span>
          <span class="choice-check" aria-hidden="true"><${Icon} name="check"/></span>
        </span>
      </label>`)}
    </div>`;
  };

  const appAbbr = (name) => {
    const clean = (name || '?').trim();
    if (!clean) return '?';
    return /[\u4e00-\u9fa5]/.test(clean[0]) ? clean[0] : clean.slice(0, 2);
  };

  const AppsList = ({ apps, appChoices }) => {
    if (apps.length === 0) return html`<p id="apps-empty" class="s-empty">${LL.t('apps.empty')}</p>`;
    return html`<ul class="app-list">${apps.map((app) => html`<li class="app" key=${app.id}>
      <span class="app-avatar" aria-hidden="true">${appAbbr(app.name)}</span>
      <div class="app-info">
        <div class="app-name-row">
          <span class="app-name">${app.name}</span>
          <span class="app-summary">${app.summary}</span>
        </div>
        <time>${app.used}</time>
      </div>
      <div class="app-ctl">
        <select class="app-select" id=${'app-mode-' + app.id} aria-label=${app.modeAria} value=${app.choice}
          onChange=${(e) => LL.send('setAppChoice', { appId: app.id, appName: app.rawName, choice: e.target.value })}>
          ${appChoices.map((opt) => html`<option key=${opt.value} value=${opt.value} selected=${opt.value === app.choice}>${opt.label}</option>`)}
        </select>
        <button type="button" class="link danger" aria-label=${LL.t('apps.forget') + ' ' + app.name}
          onClick=${() => LL.send('forgetApp', { appId: app.id })}>${LL.t('apps.forget')}</button>
      </div>
    </li>`)}</ul>`;
  };

  const General = ({ g }) => html`<div class="spage">
    <${PageHead} title=${LL.t('nav.general')} desc=${LL.t('section.general.desc')}/>

    <${Group} id="g-keys" title=${LL.t('group.keys')}>
      <${Row} id="hotkey" rowId="row-hotkey" title=${LL.t('hotkey')} desc=${LL.t('hotkey.desc')}
        extra=${g.hotkeyInvalid && html`<p class="err" role="alert">${LL.t('hotkey.invalid')}</p>`}>
        <${HotkeyField} id="hotkey" value=${g.hotkey} onChange=${set('hotkey')}/>
      <//>
      <${Row} rowId="row-commit" title=${LL.t('commit')} desc=${LL.t('commit.desc')}>
        <${SegRadio} id="commit" name="commitKey" label=${LL.t('commit')} value=${g.commitKey}
          options=${choices(['Enter', 'CtrlEnter'], ['commit.enter', 'commit.ctrlEnter'])} onChange=${set('commitKey')}/>
      <//>
    <//>

    <${Group} id="g-look" title=${LL.t('group.look')}>
      <${Row} rowId="row-theme" stacked=${true} title=${LL.t('theme')} desc=${LL.t('theme.desc')}>
        <${ThemeCards} value=${g.theme} onChange=${set('theme')}/>
      <//>
      <${Row} rowId="row-language" title=${LL.t('language')} desc=${LL.t('language.desc')}>
        <${SegRadio} id="language" name="language" label=${LL.t('language')} value=${g.language}
          options=${[['zh-CN', '中文'], ['en-US', 'English']]} onChange=${set('language')}/>
      <//>
      <${Row} id="motion" rowId="row-motion" title=${LL.t('motion')} desc=${LL.t('motion.desc')}>
        <${Switch} id="motion" label=${LL.t('motion')} checked=${g.reduceMotion} onChange=${(on) => set('reduceMotion')(String(on))}/>
      <//>
    <//>

    <${Group} id="g-apps" title=${LL.t('group.apps')}
      action=${html`<button type="button" id="apps-clear" class="link" disabled=${g.apps.length === 0} onClick=${() => LL.send('clearApps')}>${LL.t('apps.clear')}</button>`}>
      <${Row} id="remember" rowId="row-remember" title=${LL.t('remember')} desc=${LL.t('remember.desc')}>
        <${Switch} id="remember" label=${LL.t('remember')} checked=${g.rememberChoices} onChange=${(on) => set('rememberChoices')(String(on))}/>
      <//>
      <section class="apps" aria-labelledby="g-apps">
        <${AppsList} apps=${g.apps} appChoices=${g.appChoices}/>
      </section>
    <//>

    <${Group} id="g-data" title=${LL.t('group.data')}>
      <${Row} id="recent-clear" rowId="row-recent" title=${LL.t('recent')} desc=${LL.t('recent.desc')}>
        <button type="button" id="recent-clear" class="btn compact" onClick=${() => LL.send('clearRecent')}>${LL.t('recent.clear')}</button>
      <//>
    <//>
  </div>`;

  // ==================== 2. VOICE & TRANSLATION ====================
  const OutputCards = ({ value, cleanupEnabled, onChange }) => {
    const items = [
      ['Clean', LL.t('output.clean'), LL.t('output.clean.desc'), !cleanupEnabled],
      ['Translate', LL.t('output.translate'), LL.t('output.translate.desc'), false],
      ['Original', LL.t('output.original'), LL.t('output.original.desc'), false]
    ];
    return html`<div id="output" class="output-cards" role="radiogroup" aria-label=${LL.t('output')}>
      ${items.map(([v, label, desc, disabled]) => html`<label key=${v} class=${'choice-card' + (v === value ? ' on' : '') + (disabled ? ' disabled' : '')}>
        <input type="radio" name="defaultOutput" value=${v} checked=${v === value} disabled=${disabled}
          onChange=${(e) => {
            e.target.checked = v === value;
            if (v !== value && !disabled) onChange(v);
          }}/>
        <span class="choice-card-top">
          <span class="choice-card-title">${label}</span>
          <span class="choice-check" aria-hidden="true"><${Icon} name="check"/></span>
        </span>
        <span class="choice-card-desc">${desc}</span>
      </label>`)}
    </div>`;
  };

  const Voice = ({ v }) => html`<div class="spage">
    <${PageHead} title=${LL.t('nav.voice')} desc=${LL.t('section.voice.desc')}/>

    <${Group} id="v-input" title=${LL.t('group.input')}>
      <${Row} id="mic" rowId="row-mic" title=${LL.t('mic')} desc=${LL.t('mic.desc')}>
        <${Select} id="mic" value=${v.microphone} options=${v.microphones.map((name) => [name, name || LL.t('mic.default')])} onChange=${set('microphone')}/>
      <//>
      <${Row} id="auto-dictate" rowId="row-autodictate" title=${LL.t('autoDictate')} desc=${LL.t('autoDictate.desc')}>
        <${Switch} id="auto-dictate" label=${LL.t('autoDictate')} checked=${v.autoDictate} onChange=${(on) => set('autoDictate')(String(on))}/>
      <//>
      <${Row} id="preload" rowId="row-preload" title=${LL.t('preload')} desc=${LL.t('preload.desc')}>
        <${Switch} id="preload" label=${LL.t('preload')} checked=${v.preloadSpeech} onChange=${(on) => set('preloadSpeech')(String(on))}/>
      <//>
    <//>

    <${Group} id="v-output" title=${LL.t('group.output')}>
      <${Row} id="cleanup" rowId="row-cleanup" title=${LL.t('cleanup')} desc=${LL.t('cleanup.desc')}>
        <${Switch} id="cleanup" label=${LL.t('cleanup')} checked=${v.cleanup} onChange=${(on) => set('cleanup')(String(on))}/>
      <//>
      <${Row} rowId="row-output" stacked=${true} title=${LL.t('output')} desc=${LL.t('output.desc')}>
        <${OutputCards} value=${v.defaultOutput} cleanupEnabled=${v.cleanup} onChange=${set('defaultOutput')}/>
      <//>
      <${Row} rowId="row-direction" title=${LL.t('direction')} desc=${LL.t('direction.desc')}>
        <${SegRadio} id="direction" name="direction" label=${LL.t('direction')} value=${v.direction}
          options=${choices(['English', 'Chinese'], ['direction.zhEn', 'direction.enZh'])} onChange=${set('direction')}/>
      <//>
    <//>
  </div>`;

  // ==================== 3. POLISH SCENARIOS ====================
  class PolishPromptBox extends preact.Component {
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
      const openByDefault = !p.isDefault || p.invalid;
      return html`<details id="polish-advanced" class="s-disclosure polish" open=${openByDefault || undefined}>
        <summary>
          <span id="polish-title">${LL.t('polish.advanced')}</span>
          <span class="s-disclosure-sub">
            ${!p.isDefault ? html`<span class="chip mini warn">${LL.t('polish.custom')}</span>` : LL.t('polish.advanced.default')}
          </span>
        </summary>
        <div class="s-disclosure-body">
          <div class="polish-head">
            <p class="hint">${LL.t('polish.desc')}</p>
            <button type="button" id="polish-reset" class="btn compact" disabled=${p.isDefault} onClick=${this.reset}>${LL.t('polish.reset')}</button>
          </div>
          <textarea id="polish" rows="7" spellcheck="false" aria-labelledby="polish-title" aria-invalid=${p.invalid}
            value=${draft ?? p.text} onInput=${(e) => this.setState({ draft: e.target.value })} onBlur=${this.commit}></textarea>
          ${p.invalid ? html`<p class="err" role="alert">${LL.t('polish.invalid')}</p>` : html`<p class="hint">${LL.t('polish.save')}</p>`}
        </div>
      </details>`;
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
      this.state = { name: f.name, description: f.description, gate: f.gate, instructions: f.instructions };
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
      if (e.key === 'Enter' && e.target?.tagName !== 'TEXTAREA' && (e.isComposing || e.keyCode === 229)) e.preventDefault();
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

  const PolishSection = ({ v }) => {
    const enabledPresetOpts = v.presets.filter((p) => p.enabled).map((p) => [p.id, p.name]);
    return html`<div class="spage">
      <${PageHead} title=${LL.t('nav.polish')} desc=${LL.t('section.polish.desc')}
        action=${html`<button type="button" id="preset-add" class="btn primary" onClick=${() => LL.send('openPresetForm', { id: null })}>+ ${LL.t('presets.add')}</button>`}/>

      <${Group} id="p-routing" title=${LL.t('group.polishRouting')}>
        <${Row} id="default-polish" rowId="row-default-polish" title=${LL.t('defaultPolish')} desc=${LL.t('defaultPolish.desc')}>
          <${Select} id="default-polish" value=${v.defaultPolishMode} options=${enabledPresetOpts} onChange=${set('defaultPolishMode')}/>
        <//>
        <${Row} id="smart-host-polish" rowId="row-smart-host" title=${LL.t('smartHostPolish')} desc=${LL.t('smartHostPolish.desc')}>
          <${Switch} id="smart-host-polish" label=${LL.t('smartHostPolish')} checked=${v.smartHostPolish} onChange=${(on) => set('smartHostPolish')(String(on))}/>
        <//>
      <//>

      <section id="presets-section" class="sgroup presets" aria-labelledby="presets-title">
        <div class="sgroup-head">
          <h3 id="presets-title" class="sgroup-title">${LL.t('presets')} (${enabledPresetOpts.length} / ${v.presets.length})</h3>
        </div>
        <ul class="preset-grid">${v.presets.map((p) => html`<li id=${'preset-row-' + p.id}
          class=${'preset-row pcard' + (p.isDefault ? ' is-default' : '') + (!p.enabled ? ' disabled' : '')} key=${p.id}>
          <div class="pcard-head">
            <div class="pcard-title-wrap">
              <span class="pcard-ic" aria-hidden="true"><${Icon} name=${PRESET_ICON[p.id] || 'sparkle'}/></span>
              <div class="preset-main">
                <b class="preset-name">${p.name}</b>
                <div class="preset-title-row">
                  <span class=${'chip mini' + (p.builtIn ? '' : ' rec')}>${LL.t(p.builtIn ? 'presets.builtIn' : 'presets.custom')}</span>
                  <span class=${'chip mini' + (p.gate === 'FactAnchor' ? ' info' : '')}>${p.gateBadge}</span>
                  ${p.isDefault && html`<span class="chip mini ok">${LL.t('presets.defaultBadge')}</span>`}
                  ${p.builtIn && p.customized && html`<span class="chip mini warn">${LL.t('presets.tunedBadge')}</span>`}
                </div>
              </div>
            </div>
            <input type="checkbox" role="switch" id=${'preset-on-' + p.id} aria-label=${p.toggleAria}
              checked=${p.enabled} disabled=${!p.canToggle}
              onChange=${(e) => {
                e.target.checked = p.enabled;
                LL.send('togglePreset', { id: p.id, enabled: !p.enabled });
              }}/>
          </div>
          ${p.description && html`<p class="preset-desc">${p.description}</p>`}
          <div class="pcard-foot">
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
            </div>
          </div>
        </li>`)}</ul>
      </section>

      <${PolishPromptBox} p=${{ text: v.polishPrompt, isDefault: v.polishPromptIsDefault, invalid: v.polishPromptInvalid }}/>
      ${v.presetForm && html`<${PresetFormModal} key=${v.presetForm.id ?? 'add'} form=${v.presetForm} starters=${v.starterTemplates}/>`}
      ${v.deletePreset && html`<${DeletePresetDialog} d=${v.deletePreset}/>`}
    </div>`;
  };

  // ==================== 4. GLOSSARY & CORRECTIONS ====================
  class GlossarySection extends preact.Component {
    state = { tab: 'corrections', spoken: '', canonical: '', source: '', target: '' };

    addCorrection = () => {
      const spoken = this.state.spoken.trim();
      const canonical = this.state.canonical.trim();
      if (!spoken || !canonical || spoken === canonical) return;
      this.setState({ spoken: '', canonical: '' });
      LL.send('addCorrection', { spoken, canonical });
    };

    addTerm = () => {
      const { source, target } = this.state;
      if (!source.trim()) return;
      this.setState({ source: '', target: '' });
      LL.send('addTerm', { source, target });
    };

    onCorrKey = (e) => {
      if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) { e.preventDefault(); this.addCorrection(); }
    };

    onTermKey = (e) => {
      if (e.key === 'Enter' && !e.isComposing && e.keyCode !== 229) { e.preventDefault(); this.addTerm(); }
    };

    render({ v }, { tab, spoken, canonical, source, target }) {
      const isCorr = tab === 'corrections';
      return html`<div class="spage">
        <${PageHead} title=${LL.t('nav.glossary')} desc=${LL.t('section.glossary.desc')}/>

        <${Group} id="gl-builtin" title=${LL.t('group.builtinTerms')}>
          <${Row} id="builtin-terms" rowId="row-builtin-terms" title=${LL.t('builtInTerms')} desc=${LL.t('builtInTerms.desc')}
            extra=${v.builtInGlossary && html`<div class="builtin-terms-preview" aria-hidden="true">${BUILTIN_DEV_TERMS.map((t) => html`<span class="term-pill" key=${t}>${t}</span>`)}</div>`}>
            <${Switch} id="builtin-terms" label=${LL.t('builtInTerms')} checked=${v.builtInGlossary} onChange=${(on) => set('builtInGlossary')(String(on))}/>
          <//>
        <//>

        <section class="sgroup" aria-labelledby="gl-tabs">
          <div class="subtabs-bar">
            <div id="gl-tabs" class="subtab-list" role="tablist" aria-label=${LL.t('nav.glossary')}>
              <button type="button" role="tab" id="gl-tab-corrections" class="subtab-btn" aria-selected=${String(isCorr)}
                onClick=${() => this.setState({ tab: 'corrections' })}>
                <span>${LL.t('glossary.tab.corrections')}</span>
                <span class="chip mini">${v.corrections.length}</span>
              </button>
              <button type="button" role="tab" id="gl-tab-terms" class="subtab-btn" aria-selected=${String(!isCorr)}
                onClick=${() => this.setState({ tab: 'terms' })}>
                <span>${LL.t('glossary.tab.terms')}</span>
                <span class="chip mini">${v.terms.length}</span>
              </button>
            </div>
          </div>

          <div class="scard">
            ${isCorr ? html`<div class="corrections" role="tabpanel" aria-labelledby="gl-tab-corrections">
              <div class="inline-add-form">
                <label>${LL.t('corrections.spoken')}<input id="corr-spoken" value=${spoken} placeholder=${LL.t('corrections.spokenExample')} spellcheck="false"
                  onInput=${(e) => this.setState({ spoken: e.target.value })} onKeyDown=${this.onCorrKey}/></label>
                <span class="inline-arrow" aria-hidden="true">⇒</span>
                <label>${LL.t('corrections.canonical')}<input id="corr-canonical" value=${canonical} placeholder=${LL.t('corrections.canonicalExample')} spellcheck="false"
                  onInput=${(e) => this.setState({ canonical: e.target.value })} onKeyDown=${this.onCorrKey}/></label>
                <button type="button" id="corr-add" class="btn primary" disabled=${!spoken.trim() || !canonical.trim() || spoken.trim() === canonical.trim()} onClick=${this.addCorrection}>${LL.t('corrections.add')}</button>
              </div>
              <p class="lex-hint">${LL.t('corrections.hint')}</p>
              <ul class="corr-list">${v.corrections.map((item) => html`<li class="corr-item" key=${item.i}>
                <span class="lex-from">${item.spoken}</span>
                <span class="arrow" aria-hidden="true">⇒</span>
                <span class="lex-to">${item.canonical}</span>
                <button type="button" class="link danger" aria-label=${LL.t('corrections.remove') + ' ' + item.spoken}
                  onClick=${() => LL.send('removeCorrection', { index: item.i })}>${LL.t('corrections.remove')}</button>
              </li>`)}</ul>
            </div>` : html`<div class="terms" role="tabpanel" aria-labelledby="gl-tab-terms">
              <div class="inline-add-form">
                <label>${LL.t('terms.source')}<input id="term-source" value=${source} placeholder=${LL.t('terms.sourceExample')} spellcheck="false"
                  onInput=${(e) => this.setState({ source: e.target.value })} onKeyDown=${this.onTermKey}/></label>
                <span class="inline-arrow" aria-hidden="true">→</span>
                <label>${LL.t('terms.target')}<input id="term-target" value=${target} placeholder=${LL.t('terms.targetExample')} spellcheck="false"
                  onInput=${(e) => this.setState({ target: e.target.value })} onKeyDown=${this.onTermKey}/></label>
                <button type="button" id="term-add" class="btn primary" disabled=${!source.trim()} onClick=${this.addTerm}>${LL.t('terms.add')}</button>
              </div>
              <p class="lex-hint">${LL.t('terms.hint')}</p>
              <ul class="term-list">${v.terms.map((term) => html`<li class="term" key=${term.i}>
                <span class="lex-from">${term.source}</span>
                <span class="arrow" aria-hidden="true">→</span>
                <span class="lex-to">${term.target}</span>
                <span class=${'chip mini' + (term.source === term.target ? ' info' : '')}>${LL.t(term.source === term.target ? 'glossary.keepSame' : 'glossary.translated')}</span>
                <button type="button" class="link danger" aria-label=${LL.t('terms.remove') + ' ' + term.source}
                  onClick=${() => LL.send('removeTerm', { index: term.i })}>${LL.t('terms.remove')}</button>
              </li>`)}</ul>
            </div>`}
          </div>
        </section>
      </div>`;
    }
  }

  // ==================== SHARED MODAL DIALOG ====================
  const FORM_FIELD = { nameMissing: 'ep-name', nameTaken: 'ep-name', urlInvalid: 'ep-url', modelMissing: 'ep-model', keyMissing: 'ep-key' };

  class Modal extends preact.Component {
    componentDidMount() {
      this.opener = document.activeElement;
      this.pane.querySelector('[data-initial]')?.focus();
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
        if (items.length === 0) return;
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
            <button type="button" class="icon-btn" aria-label=${LL.t('closeAria')} onClick=${onClose}><${Icon} name="close"/></button></header>
          ${children}
        </section>
      </div>`;
    }
  }

  // ==================== 5. MODELS & SERVICES ====================
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
      onClick=${(e) => { if (!e.target.closest('button, input, label')) choose(); }}>
      <div class="mcard-top">
        <input type="radio" class="mradio" id=${radioId} name=${'stage-' + stage}
          aria-label=${opt.name} checked=${opt.active} disabled=${!opt.canSelectImmediate && !opt.active}
          onChange=${(e) => { e.target.checked = opt.active; choose(); }}/>
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
            class=${'btn compact' + (opt.active ? ' primary' : ' outline')}
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
    const choose = () => { if (canChoose) LL.send('selectCloudModel', { step, endpointId: ep.id }); };
    return html`<li id=${'model-cloud-card-' + step + '-' + ep.id}
      class=${'mcard cloud' + (ep.active ? ' active' : '') + (!ep.usable && !ep.active ? ' unsupported' : '') + (canChoose ? ' selectable' : '')}
      onClick=${(e) => { if (!e.target.closest('button, input, label')) choose(); }}>
      <div class="mcard-top">
        <input type="radio" class="mradio" id=${radioId} name=${'stage-' + stage}
          aria-label=${ep.name + ' · ' + ep.model} checked=${ep.active} disabled=${!canChoose && !ep.active}
          onChange=${(e) => { e.target.checked = ep.active; choose(); }}/>
        <div class="mcard-body">
          <div class="mcard-head">
            <span class="mrank cloud">${LL.t('models.cloud.badge')}</span>
            <label class="mname" for=${radioId}>${ep.name}</label>
            <span class="chip mini">${ep.model}</span>
            ${ep.active && html`<span class="chip mini ok">${LL.t('models.state.activeInstalled')}</span>`}
            ${ep.peerBadge && html`<span class="chip mini">${ep.peerBadge}</span>`}
            ${!ep.usable && html`<span class="chip mini warn">${LL.t('models.cloud.incomplete')}</span>`}
          </div>
          <p class="mcard-desc"><span>${ep.host} · ${ep.hasKey ? LL.t('cloud.keySaved') : LL.t('cloud.keyMissing')}</span></p>
          <p class="mcard-foot"><span>${LL.t(stage === 'speech' ? 'models.cloud.speechFootprint' : 'models.cloud.footprint')}</span></p>
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

  const hostSuitabilityClass = (s) =>
    s === 'catalogverified' || s === 'flagshipbilingual' ? 'chip mini rec'
    : s === 'suboptimalwarning' ? 'chip mini warn'
    : 'chip mini';

  const stageEnum = (stage) => stage === 'speech' ? 'Speech' : stage === 'translator' ? 'Translator' : 'Cleaner';

  class LocalHostSection extends preact.Component {
    state = { expandedHosts: {}, addingCustom: false, customName: '', customUrl: 'http://127.0.0.1:8080', tokenDrafts: {}, copiedCmd: null };

    toggleExpand = (hostId) => this.setState((s) => ({
      expandedHosts: { ...s.expandedHosts, [hostId]: !s.expandedHosts[hostId] }
    }));

    copyCmd = (cmd) => {
      if (navigator.clipboard?.writeText) navigator.clipboard.writeText(cmd).catch(() => {});
      this.setState({ copiedCmd: cmd });
      setTimeout(() => { if (this.state.copiedCmd === cmd) this.setState({ copiedCmd: null }); }, 1500);
    };

    submitCustom = (e) => {
      e.preventDefault();
      const { customName, customUrl } = this.state;
      if (!customUrl.trim()) return;
      LL.send('addCustomLocalHost', { label: customName.trim(), baseUrl: customUrl.trim() });
      this.setState({ addingCustom: false, customName: '', customUrl: 'http://127.0.0.1:8080' });
    };

    saveToken = (hostId, address) => {
      const token = (this.state.tokenDrafts[hostId] || '').trim();
      if (!token) return;
      LL.send('saveLocalHostConfig', { hostId, baseUrl: address ? ('http://' + address) : '', apiToken: token, clearToken: false });
      this.setState((s) => ({ tokenDrafts: { ...s.tokenDrafts, [hostId]: '' } }));
    };

    render({ stage, hosts, refreshing }, { expandedHosts, addingCustom, customName, customUrl, tokenDrafts, copiedCmd }) {
      const enumStage = stageEnum(stage);
      return html`<div class="msource-group lhost-group">
        <div class="msource-head">
          <h3 class="msource-title">${LL.t('models.group.localHost.title')}</h3>
          <div class="msource-links">
            <button type="button" id=${'stage-lhost-refresh-' + stage} class="link" disabled=${refreshing}
              onClick=${() => LL.send('refreshLocalHosts')}>${LL.t(refreshing ? 'models.localHost.refreshing' : 'models.localHost.refresh')}</button>
            <button type="button" id=${'stage-lhost-add-' + stage} class="link"
              onClick=${() => this.setState({ addingCustom: !addingCustom })}>+ ${LL.t('models.localHost.addCustom')}</button>
          </div>
        </div>
        ${addingCustom && html`<form class="lhost-custom-form" onSubmit=${this.submitCustom}>
          <input id=${'lhost-custom-name-' + stage} value=${customName} maxlength="40" aria-label=${LL.t('models.localHost.customName')} placeholder=${LL.t('models.localHost.customName')}
            onInput=${(e) => this.setState({ customName: e.target.value })}/>
          <input id=${'lhost-custom-url-' + stage} type="url" value=${customUrl} aria-label=${LL.t('models.localHost.customUrl')} placeholder=${LL.t('models.localHost.customUrlHint')}
            onInput=${(e) => this.setState({ customUrl: e.target.value })}/>
          <button type="submit" id=${'lhost-custom-submit-' + stage} class="btn compact primary">${LL.t('models.localHost.customAdd')}</button>
        </form>`}
        ${(!hosts || hosts.length === 0)
          ? html`<div id=${'stage-lhost-empty-' + stage} class="mcloud-empty">
              <span>${LL.t(stage === 'speech' ? 'models.localHost.speechEmpty' : 'models.localHost.empty')}</span>
              <button type="button" class="btn compact" onClick=${() => LL.send('refreshLocalHosts')}>${LL.t('models.localHost.refresh')}</button>
            </div>`
          : hosts.map((host) => {
              const expanded = !!expandedHosts[host.hostId];
              const visibleModels = expanded ? host.models : host.models.slice(0, 4);
              const hiddenCount = Math.max(0, host.models.length - visibleModels.length);
              return html`<div key=${host.hostId} id=${'lhost-box-' + stage + '-' + host.hostId} class="lhost-box">
                <div class="lhost-head">
                  <div class="lhost-title">
                    <b>${host.name}</b>
                    ${host.address && html`<code class="lhost-addr">${host.address}</code>`}
                    <span class=${'chip mini ' + (host.status === 'running' ? 'ok' : host.status === 'authrequired' ? 'warn' : '')}>${host.statusBadge}</span>
                  </div>
                  <div class="lhost-actions">
                    ${host.status === 'installednotserving' && host.supportsStartServer && html`<button type="button" class="btn compact primary"
                      onClick=${() => LL.send('startLocalHostServer', { hostId: host.hostId })}>${LL.t('models.localHost.startServer')}</button>`}
                    ${host.supportsOpenApp && html`<button type="button" class="btn compact"
                      onClick=${() => LL.send('openLocalHostApp', { hostId: host.hostId })}>${LL.t('models.localHost.openApp')}</button>`}
                    ${host.isCustom && html`<button type="button" class="link danger"
                      onClick=${() => LL.send('removeCustomLocalHost', { hostId: host.hostId })}>${LL.t('models.localHost.remove')}</button>`}
                  </div>
                </div>
                ${host.status === 'authrequired' && html`<div class="lhost-notice">
                  <p>${LL.t('models.localHost.authPrompt')}</p>
                  <div class="lhost-token-row">
                    <input type="password" aria-label=${LL.t('models.localHost.tokenPlaceholder')} value=${tokenDrafts[host.hostId] || ''} placeholder=${LL.t('models.localHost.tokenPlaceholder')}
                      onInput=${(e) => this.setState({ tokenDrafts: { ...tokenDrafts, [host.hostId]: e.target.value } })}/>
                    <button type="button" class="btn compact primary" onClick=${() => this.saveToken(host.hostId, host.address)}>${LL.t('models.localHost.saveToken')}</button>
                  </div>
                </div>`}
                ${host.status === 'installednotserving' && html`<p class="hint lhost-hint">${LL.t('models.localHost.notServingHint')}</p>`}
                ${host.status === 'running' && host.models.length > 0 && html`<ul class="mcard-list">${visibleModels.map((m) => {
                  const radioId = 'model-lhost-' + stage + '-' + host.hostId + '-' + m.key.replace(/[^a-zA-Z0-9_-]/g, '_');
                  const choose = () => { if (m.canSelect) LL.send('selectLocalHostModel', { stage: enumStage, hostId: host.hostId, modelKey: m.key }); };
                  return html`<li key=${m.key} class=${'mcard lhost' + (m.active ? ' active' : '') + (m.canSelect ? ' selectable' : '')}
                    onClick=${(e) => { if (!e.target.closest('button, input, label')) choose(); }}>
                    <div class="mcard-top">
                      <input type="radio" class="mradio" id=${radioId} name=${'stage-' + stage}
                        aria-label=${host.name + ' · ' + m.name} checked=${m.active} disabled=${!m.canSelect && !m.active}
                        onChange=${(e) => { e.target.checked = m.active; choose(); }}/>
                      <div class="mcard-body">
                        <div class="mcard-head">
                          <span class="mrank lhost">${LL.t('models.localHost.badge.host')}</span>
                          <label class="mname" for=${radioId}>${m.name}</label>
                          <span class=${hostSuitabilityClass(m.suitability)}>${m.suitabilityBadge}</span>
                          <span class=${'chip mini' + (m.isLoaded ? ' ok' : '')}>${m.loadedBadge}</span>
                          ${m.parallelBadge && html`<span class="chip mini">${m.parallelBadge}</span>`}
                          ${m.active && html`<span class="chip mini ok">${LL.t('models.state.activeInstalled')}</span>`}
                          ${m.peerBadge && html`<span class="chip mini">${m.peerBadge}</span>`}
                        </div>
                        ${m.meta && html`<p class="mcard-foot"><span>${m.meta}</span></p>`}
                        ${m.warningText && html`<p class="mcard-foot"><span class="mfit-note heavy">${m.warningText}</span></p>`}
                      </div>
                      <div class="mcard-actions">
                        ${m.canSelect && html`<button type="button" class="btn compact" onClick=${choose}>${LL.t('models.action.enable')}</button>`}
                      </div>
                    </div>
                  </li>`;
                })}</ul>`}
                ${hiddenCount > 0 && html`<button type="button" class="link lhost-more" onClick=${() => this.toggleExpand(host.hostId)}>
                  ${fmt('models.localHost.showMore', hiddenCount)}</button>`}
                ${expanded && host.models.length > 4 && html`<button type="button" class="link lhost-more" onClick=${() => this.toggleExpand(host.hostId)}>
                  ${LL.t('models.localHost.showLess')}</button>`}
                ${host.status === 'running' && !host.hasSuitableModels && host.recommendations.length > 0 && html`<div class="lhost-missing">
                  <strong>${LL.t('models.localHost.missingTitle')}</strong>
                  <p class="hint">${LL.t('models.localHost.missingDesc')}</p>
                  <ul class="lhost-rec-list">${host.recommendations.map((rec) => html`<li key=${rec.catalogModelId} class="lhost-rec">
                    <div class="lhost-rec-info">
                      <b>${rec.name}</b>
                      <span class="hint">${rec.sizeText}</span>
                      ${rec.error && html`<span class="err">${rec.error}</span>`}
                    </div>
                    <div class="lhost-rec-actions">
                      ${rec.canDownload && html`<button type="button" class="btn compact primary"
                        onClick=${() => LL.send('downloadLocalHostModel', { hostId: host.hostId, stage: enumStage, catalogModelId: rec.catalogModelId })}>
                        ${LL.t('models.localHost.downloadInHost')}</button>`}
                      ${rec.downloading && html`<span class="chip mini rec">${rec.statusText}</span>`}
                      <button type="button" class="btn compact" onClick=${() => this.copyCmd(rec.copyCommand)}>
                        ${copiedCmd === rec.copyCommand ? LL.t('models.localHost.copied') : LL.t('models.localHost.copyCmd')}</button>
                    </div>
                  </li>`)}</ul>
                </div>`}
              </div>`;
            })}
      </div>`;
    }
  }

  class ModelStageSection extends preact.Component {
    constructor(props) {
      super(props);
      this.state = { source: this.defaultSource(props.group, props.hasCloudModal) };
    }

    defaultSource(group, hasCloudModal) {
      if (group.cloudRouted || group.cloudInvalid || hasCloudModal) return 'cloud';
      if (group.localHostRouted) return 'host';
      return 'local';
    }

    componentDidUpdate(prevProps) {
      const g = this.props.group;
      const p = prevProps.group;
      if (g.stage !== p.stage) {
        this.setState({ source: this.defaultSource(g, this.props.hasCloudModal) });
      } else if ((g.cloudRouted && !p.cloudRouted) || (g.cloudInvalid && !p.cloudInvalid) || (this.props.hasCloudModal && !prevProps.hasCloudModal)) {
        this.setState({ source: 'cloud' });
      } else if (g.localHostRouted && !p.localHostRouted) {
        this.setState({ source: 'host' });
      }
    }

    render({ group, localHostRefreshing }, { source }) {
      const compatible = group.options.filter((o) => o.fit !== 'unsupported' || o.active);
      const unsupported = group.options.filter((o) => o.fit === 'unsupported' && !o.active);
      const activeSource = group.cloudRouted ? 'cloud' : group.localHostRouted ? 'host' : 'local';
      const hosts = group.localHosts || [];
      const runningHosts = hosts.filter((h) => h.status === 'running').length;
      const cloudCount = (group.cloudOptions || []).length;
      const showLocal = source === 'local';
      const showHost = source === 'host' && group.localHostSupported;
      const showCloud = source === 'cloud' && group.cloudStep;

      return html`<section id=${'stage-' + group.stage} class="mstage" role="tabpanel" aria-labelledby=${'model-tab-' + group.stage}>
        ${group.localOnly && html`<div id="speech-local-only" class="mstage-local-pill" role="note">
          <span class="chip mini ok">${LL.t('privacy.badge.alwaysLocal')}</span>
          <span>${LL.t('models.speech.localOnly')}</span>
        </div>`}
        ${group.notice && html`<div class="mstage-notice" role="status"><span>${group.notice}</span></div>`}

        <div class="source-bar">
          <div class="source-seg" role="group" aria-label=${LL.t('models.source.aria')}>
            <button type="button" id=${'source-local-' + group.stage} aria-pressed=${String(showLocal)}
              onClick=${() => this.setState({ source: 'local' })}>
              <${Icon} name="cpu"/>
              <span>${LL.t('models.source.local')}</span>
              <span class="src-count">${compatible.length}</span>
              ${activeSource === 'local' && html`<span class="src-active-dot" aria-hidden="true"></span>`}
            </button>
            ${group.localHostSupported && html`<button type="button" id=${'source-host-' + group.stage} aria-pressed=${String(showHost)}
              onClick=${() => this.setState({ source: 'host' })}>
              <${Icon} name="server"/>
              <span>${LL.t('models.source.host')}</span>
              <span class="src-count">${runningHosts}</span>
              ${activeSource === 'host' && html`<span class="src-active-dot" aria-hidden="true"></span>`}
            </button>`}
            ${group.cloudStep && html`<button type="button" id=${'source-cloud-' + group.stage} aria-pressed=${String(showCloud)}
              onClick=${() => this.setState({ source: 'cloud' })}>
              <${Icon} name="cloud"/>
              <span>${LL.t('models.source.cloud')}</span>
              <span class="src-count">${cloudCount}</span>
              ${activeSource === 'cloud' && html`<span class="src-active-dot" aria-hidden="true"></span>`}
            </button>`}
          </div>
          ${showCloud && html`<button type="button" id=${'stage-cloud-add-' + group.stage} class="btn compact primary"
            onClick=${() => LL.send('openEndpointForm', { id: null })}>+ ${LL.t('models.cloud.add')}</button>`}
        </div>

        <p class="mstage-sub">${group.desc} · <b>${LL.t('models.active.label')}：${group.tabSummary}</b></p>

        ${showHost && html`<${LocalHostSection} stage=${group.stage} hosts=${hosts} refreshing=${localHostRefreshing}/>`}

        ${showCloud && html`<div class="msource-group">
          ${group.cloudInvalid && html`<p class="err msource-err" role="alert">${LL.t('cloud.invalid')}</p>`}
          ${cloudCount === 0
            ? html`<div id=${'stage-cloud-empty-' + group.stage} class="mcloud-empty">
                <span>${LL.t(group.stage === 'speech' ? 'models.cloud.speechEmpty' : 'models.cloud.empty')}</span>
                <button type="button" id=${'stage-cloud-empty-add-' + group.stage} class="btn compact primary"
                  onClick=${() => LL.send('openEndpointForm', { id: null })}>${LL.t('models.cloud.add')}</button>
              </div>`
            : html`<ul class="mcard-list">${group.cloudOptions.map((ep) =>
                html`<${CloudModelCard} key=${ep.id} stage=${group.stage} step=${group.cloudStep} ep=${ep}/>`)}</ul>`}
        </div>`}

        ${showLocal && html`<div class="msource-group">
          <ul class="mcard-list">${compatible.map((opt) => html`<${ModelCard} key=${opt.id} stage=${group.stage} opt=${opt}/>`)}</ul>
          ${unsupported.length > 0 && html`<details class="s-disclosure m-unsupported">
            <summary><span>${fmt('models.unsupported.toggle', unsupported.length)}</span></summary>
            <div class="s-disclosure-body">
              <ul class="mcard-list">${unsupported.map((opt) => html`<${ModelCard} key=${opt.id} stage=${group.stage} opt=${opt}/>`)}</ul>
            </div>
          </details>`}
        </div>`}
      </section>`;
    }
  }

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

  const ModelStorageSection = ({ s }) => html`<section id="models-storage" class="mstorage" role="tabpanel" aria-labelledby="model-tab-storage">
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
      <h3 class="storage-subhead">${LL.t('models.storage.listTitle')} (${s.items.length})</h3>
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
    <div class="mhw-col">
      <div class="mhw-top">
        <h3 id="models-hw-title" class="sgroup-title">${LL.t('group.hw')}</h3>
        <div class="mhw-cta">
          ${hw.isRecommended
            ? html`<span id="models-best-badge" class="chip mini ok">${LL.t('models.hw.matched')}</span>`
            : hw.isRecommendedDownloading
            ? html`<span id="models-best-preparing" class="chip mini rec" role="status">${LL.t('models.hw.preparing')}</span>`
            : html`<button type="button" id="models-apply-best" class="link accent"
                onClick=${() => LL.send('applyRecommendedModels')}>${hw.recommendActionLabel}</button>`}
        </div>
      </div>
      <p id="models-hw-summary" class="mhw-summary">${hw.summary}</p>
      <div class="mhw-bar-row">
        <div class=${'progress' + (hw.overBudget ? ' warn' : ' ok')} role="progressbar" aria-label=${LL.t('models.hw.gaugeAria')}
          aria-valuemin="0" aria-valuemax="100" aria-valuenow=${hw.budgetPercent}>
          <span style=${{ width: hw.budgetPercent + '%' }}></span>
        </div>
        <span class="mhw-meta">${hw.residency}</span>
      </div>
      <p class=${'mhw-note' + (hw.overBudget ? ' warn' : '')}>${hw.modeNote}</p>
    </div>

    <div id="models-privacy-strip" class="mhw-col mhw-privacy-col">
      <div class="mhw-top">
        <h3 class="sgroup-title">${LL.t('group.privacy')}</h3>
        <span class=${'chip mini ' + (hw.anyCloud ? 'warn' : 'ok')}>${LL.t(hw.anyCloud ? 'privacy.badge.cloud' : 'privacy.badge.local')}</span>
      </div>
      <p class="mhw-note">${hw.privacyStatus}</p>
      <div class="mhw-privacy-actions">
        ${hw.anyCloud && html`<button type="button" id="cloud-disable-all" class="btn compact outline"
          onClick=${() => LL.send('disableAllCloud')}>${LL.t('privacy.routing.allLocal')}</button>`}
        <button type="button" id="models-disk-link" class="link accent mhw-disk-btn" onClick=${onOpenStorage}>${hw.diskUsed} →</button>
      </div>
    </div>
  </section>`;

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

  const endpointAnswer = (allow) => LL.send('endpointAnswer', { allow });

  const DeleteEndpointDialog = ({ d, stage }) => html`<${Modal} id="endpoint-change" title=${fmt('cloud.delete.title', d.name)}
    returnTo=${'stage-cloud-delete-' + stage + '-' + d.id} onClose=${() => endpointAnswer(false)}>
    <p>${LL.t('cloud.delete.body') + (d.affectedSteps ? ' ' + fmt('cloud.delete.steps', d.affectedSteps) : '')}</p>
    <div class="dlg-actions">
      <button type="button" id="endpoint-keep" class="btn" data-initial onClick=${() => endpointAnswer(false)}>${LL.t('cloud.delete.keep')}</button>
      <button type="button" id="endpoint-confirm" class="btn primary destructive" onClick=${() => endpointAnswer(true)}>${LL.t('cloud.delete.confirm')}</button>
    </div>
  <//>`;

  const STAGE_ICON = { speech: 'mic', cleaner: 'sparkle', translator: 'globe', storage: 'drive' };

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
          tone: g.cloudRouted ? 'warn' : g.localHostRouted ? 'host' : 'ok',
          panelId: 'stage-' + g.stage
        })),
        {
          id: 'storage',
          title: m.storage.tabTitle,
          summary: m.storage.tabSummary,
          busy: false,
          tone: null,
          panelId: 'models-storage'
        }
      ];
      const activeStage = m.stages.find((g) => g.stage === tab);
      const cloudStage = (tab === 'speech' || tab === 'cleaner' || tab === 'translator') ? tab : 'speech';
      const cloudEndpoints = m.stages.find((g) => g.stage === cloudStage)?.cloudOptions
        ?? m.stages.find((g) => g.cloudStep)?.cloudOptions
        ?? [];

      return html`<div class="spage models-page">
        <${PageHead} title=${LL.t('nav.models')} desc=${LL.t('section.models.desc')}/>
        <${HardwareBanner} hw=${m.hardware} onOpenStorage=${() => this.selectTab('storage')}/>
        ${m.hotHost && html`<section id="models-hot-host" class="lhost-hot-banner" role="status">
          <div class="lhost-hot-info">
            <span class="chip mini host">${LL.t('models.localHost.hotBanner.badge')}</span>
            <span>${m.hotHost.summary}</span>
          </div>
          <button type="button" id="models-hot-host-apply" class="btn compact primary"
            onClick=${() => LL.send('applyHotLocalHostModel', { hostId: m.hotHost.hostId, modelKey: m.hotHost.modelKey })}>
            ${LL.t('models.localHost.hotBanner.apply')}</button>
        </section>`}
        <${DownloadCenter} downloads=${m.downloads}/>
        <div id="models-tabs" class="mtabs" role="tablist" aria-label=${LL.t('models.tabs.aria')}
          onKeyDown=${this.onTabKeyDown(tabs)}>
          ${tabs.map((t) => html`<button type="button" role="tab" key=${t.id} id=${'model-tab-' + t.id}
            class=${'mtab' + (tab === t.id ? ' on' : '') + (t.busy ? ' busy' : '')}
            aria-selected=${String(tab === t.id)} aria-controls=${t.panelId}
            tabIndex=${tab === t.id ? 0 : -1}
            onClick=${() => this.selectTab(t.id)}>
            <span class="mtab-top">
              <span class="mtab-title"><${Icon} name=${STAGE_ICON[t.id] || 'cpu'}/><span>${t.title}</span></span>
              ${t.tone && html`<i class=${'s-dot ' + t.tone} aria-hidden="true"></i>`}
            </span>
            <span class="mtab-sub">${t.summary}</span>
          </button>`)}
        </div>
        ${activeStage ? html`<${ModelStageSection} key=${activeStage.stage} group=${activeStage} localHostRefreshing=${m.localHostRefreshing}
            hasCloudModal=${Boolean(m.consent || m.form || m.deleteConfirm)}/>`
          : html`<${ModelStorageSection} s=${m.storage}/>`}
        ${m.consent && html`<${Consent} c=${m.consent}/>`}
        ${m.form && html`<${EndpointForm} form=${m.form} stage=${cloudStage} endpoint=${cloudEndpoints.find((e) => e.id === m.form.id)}/>`}
        ${m.deleteConfirm && html`<${DeleteEndpointDialog} d=${m.deleteConfirm} stage=${cloudStage}/>`}
      </div>`;
    }
  }

  // ==================== 6. ABOUT & DIAGNOSTICS ====================
  class Diagnostics extends preact.Component {
    state = { open: false };
    show = () => this.setState({ open: true });
    hide = () => this.setState({ open: false });

    render({ lines }, { open }) {
      return html`<${Row} id="diag-open" title=${LL.t('diag')} desc=${LL.t('diag.desc')}>
        <button type="button" id="diag-open" class="btn compact outline" onClick=${this.show}>${LL.t('diag.open')}</button>
      <//>
      ${open && html`<${Modal} id="diag" title=${LL.t('diag.title')} returnTo="diag-open" onClose=${this.hide}>
        <dl id="diag-text" class="diag">${lines.map((line) => html`<div key=${line.label}><dt>${line.label}</dt><dd>${line.value}</dd></div>`)}</dl>
        <div class="dlg-actions"><button type="button" id="diag-close" class="btn primary" data-initial onClick=${this.hide}>${LL.t('closeAria')}</button></div>
      <//>`}`;
    }
  }

  const Update = ({ u }) => html`<${Row} id="update-action" title=${LL.t('update')} desc=${LL.t('update.desc')}
    extra=${html`<p id="update-status" role="status" aria-live="polite">${u.status}</p>
      ${u.busy && html`<div id="update-progress" class=${'progress' + (u.percent === null ? ' indeterminate' : '')} role="progressbar"
        aria-valuemin="0" aria-valuemax="100" aria-valuenow=${u.percent === null ? undefined : u.percent}>
        <span style=${u.percent === null ? undefined : { width: u.percent + '%' }}></span></div>`}`}>
    ${(u.canCheck || u.busy) && html`<button type="button" id="update-action" class="btn compact" disabled=${u.busy} onClick=${() => LL.send('checkUpdates')}>${LL.t('update.check')}</button>`}
    ${u.canRestart && html`<button type="button" id="update-action" class="btn compact primary" onClick=${() => LL.send('restartToUpdate')}>${LL.t('update.restart')}</button>`}
    ${u.canDownload && html`<button type="button" id="update-action" class="btn compact primary" onClick=${() => LL.send('openDownloads')}>${LL.t('update.download')}</button>`}
  <//>`;

  const About = ({ a }) => html`<div class="spage about-page">
    <${PageHead} title=${LL.t('nav.about')} desc=${LL.t('section.about.desc')}/>
    <section class="about-hero" aria-labelledby="about-app-name">
      <div class="about-brand">
        <span class="about-logo" aria-hidden="true">li</span>
        <div>
          <h3 id="about-app-name" class="about-title">${LL.t('appName')}</h3>
          <p class="hint">${LL.t('about.subtitle')}</p>
        </div>
      </div>
      <span id="version" class="chip rec">${a.version}</span>
    </section>
    <${Group} id="a-update" title=${LL.t('group.update')}>
      <${Update} u=${a.update}/>
      <${Diagnostics} lines=${a.diagnostics}/>
    <//>
  </div>`;

  // ==================== ROOT SHELL WITH SIDEBAR & SEARCH ====================
  const SEARCH_ITEMS = [
    ['general', 'hotkey', 'hotkey.desc'],
    ['general', 'commit', 'commit.desc'],
    ['general', 'theme', 'theme.desc'],
    ['general', 'language', 'language.desc'],
    ['general', 'motion', 'motion.desc'],
    ['general', 'remember', 'remember.desc'],
    ['general', 'recent', 'recent.desc'],
    ['voice', 'mic', 'mic.desc'],
    ['voice', 'autoDictate', 'autoDictate.desc'],
    ['voice', 'preload', 'preload.desc'],
    ['voice', 'cleanup', 'cleanup.desc'],
    ['voice', 'output', 'output.desc'],
    ['voice', 'direction', 'direction.desc'],
    ['polish', 'defaultPolish', 'defaultPolish.desc'],
    ['polish', 'smartHostPolish', 'smartHostPolish.desc'],
    ['polish', 'presets', 'presets.desc'],
    ['polish', 'polish', 'polish.desc'],
    ['glossary', 'builtInTerms', 'builtInTerms.desc'],
    ['glossary', 'corrections', 'corrections.hint'],
    ['glossary', 'terms', 'terms.hint'],
    ['models', 'nav.models', 'section.models.desc'],
    ['about', 'update', 'update.desc'],
    ['about', 'diag', 'diag.desc']
  ];

  class SettingsShell extends preact.Component {
    state = { query: '' };

    componentDidMount() {
      document.addEventListener('keydown', this.onGlobalKey);
    }

    componentWillUnmount() {
      document.removeEventListener('keydown', this.onGlobalKey);
    }

    onGlobalKey = (e) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        this.searchEl?.focus();
        this.searchEl?.select();
      }
    };

    jump = (section) => {
      this.setState({ query: '' });
      LL.send('openSettings', { section });
    };

    render(_, { query }) {
      const s = LL.state.settings;
      if (!s) return null;

      const enabledPresets = s.voice.presets.filter((p) => p.enabled).length;
      const lexCount = s.voice.corrections.length + s.voice.terms.length;
      const q = query.trim().toLowerCase();
      const hits = q
        ? SEARCH_ITEMS.filter(([sec, titleKey, descKey]) =>
            (LL.t(titleKey) + ' ' + LL.t(descKey) + ' ' + LL.t('nav.' + sec)).toLowerCase().includes(q)
          ).slice(0, 6)
        : [];

      const navButton = ([id, key, icon]) => html`<button type="button" key=${id} id=${'nav-' + id}
        class=${'s-nav-btn' + (id === 'about' ? ' nav-about' : '')}
        aria-current=${s.section === id ? 'page' : undefined}
        onClick=${() => LL.send('openSettings', { section: id })}>
        <${Icon} name=${icon}/>
        <span class="s-nav-label">${LL.t(key)}</span>
        ${id === 'polish' && html`<span class="s-nav-count">${enabledPresets}</span>`}
        ${id === 'glossary' && html`<span class="s-nav-count">${lexCount}</span>`}
        ${id === 'models' && (s.models.navBadge
          ? html`<span id="nav-models-badge" class="nav-badge">${s.models.navBadge}</span>`
          : html`<i class=${'s-dot ' + (s.models.hardware.anyCloud ? 'warn' : 'ok')} aria-hidden="true"></i>`)}
      </button>`;

      return html`<div class="settings">
        <aside class="s-sidebar">
          <div class="s-search" role="search">
            <${Icon} name="search"/>
            <input id="settings-search" type="search" value=${query}
              aria-label=${LL.t('search.placeholder')} placeholder=${LL.t('search.placeholder')}
              autocomplete="off" autocorrect="off" autocapitalize="none" spellcheck="false"
              ref=${(el) => (this.searchEl = el)}
              onInput=${(e) => this.setState({ query: e.target.value })}
              onKeyDown=${(e) => { if (e.key === 'Escape') { this.setState({ query: '' }); e.target.blur(); } }}/>
          </div>
          ${q && html`<div id="settings-search-results" class="s-search-results">
            ${hits.length === 0
              ? html`<p class="s-search-empty">${LL.t('search.empty')}</p>`
              : hits.map(([sec, titleKey]) => html`<button type="button" key=${sec + ':' + titleKey} class="s-search-hit"
                  onClick=${() => this.jump(sec)}>
                  <b>${LL.t(titleKey)}</b>
                  <span>${LL.t('nav.' + sec)}</span>
                </button>`)}
          </div>`}
          <nav aria-label=${LL.t('nav.aria')}>
            ${NAV_GROUPS.map((g) => html`<div class="s-nav-group" key=${g.key}>
              <div class="s-nav-group-label" aria-hidden="true">${LL.t(g.key)}</div>
              ${g.items.map(navButton)}
            </div>`)}
            <div class="s-nav-spacer" aria-hidden="true"></div>
            ${navButton(['about', 'nav.about', 'info'])}
          </nav>
        </aside>
        <main id="panel"><div class="panel-body">
          ${s.section === 'general' && html`<${General} g=${s.general}/>`}
          ${s.section === 'voice' && html`<${Voice} v=${s.voice}/>`}
          ${s.section === 'polish' && html`<${PolishSection} v=${s.voice}/>`}
          ${s.section === 'glossary' && html`<${GlossarySection} v=${s.voice}/>`}
          ${s.section === 'models' && html`<${Models} m=${s.models}/>`}
          ${s.section === 'about' && html`<${About} a=${s.about}/>`}
        </div></main>
      </div>`;
    }
  }

  LL.Settings = () => html`<${SettingsShell}/>`;
})();
