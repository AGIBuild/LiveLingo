// The source pane. The text box belongs to the page while the user types (design 5.3): a snapshot replaces its text only
// when C# changed the text behind the page's back (speech, import, a restored draft), never as an echo of typing.
(function () {
  'use strict';

  const html = LL.html;

  function autosize(el) {
    if (el.hidden) return;
    const top = el.scrollTop;
    // Measure natural content height without flex-grow stretching or classic-scrollbar width shrinkage during collapse.
    el.style.flex = '0 0 auto';
    el.style.overflowY = 'hidden';
    el.style.height = 'auto';
    const natural = el.scrollHeight;
    el.style.flex = '';
    el.style.overflowY = '';
    el.style.height = natural + 'px';
    el.scrollTop = top;
  }

  class Source extends preact.Component {
    state = { expanded: false };
    composing = false;
    lastSent = '';
    lastWidth = 0;
    stickLive = true;

    setBox = (el) => {
      this.box = el;
    };

    setLive = (el) => {
      this.live = el;
    };

    onLiveScroll = () => {
      const el = this.live;
      if (!el) return;
      this.stickLive = el.scrollHeight - el.scrollTop - el.clientHeight <= 32;
    };

    onWindowResize = () => {
      const w = this.box?.clientWidth ?? 0;
      if (w > 0 && w !== this.lastWidth) {
        this.lastWidth = w;
        autosize(this.box);
      }
    };

    componentDidMount() {
      window.addEventListener('resize', this.onWindowResize);
      this.sync();
      this.lastWidth = this.box?.clientWidth ?? 0;
    }

    componentWillUnmount() {
      window.removeEventListener('resize', this.onWindowResize);
    }

    componentDidUpdate() {
      this.sync();
    }

    sync() {
      const { source } = this.props;
      const s = LL.state;
      const box = this.box;
      if (source.sourceRev > s.sentRev && !this.composing) {
        s.sentRev = source.sourceRev;
        if (box.value !== source.text) box.value = source.text;
        this.lastSent = box.value;
      }
      autosize(box);
      if (source.mode !== 'live') {
        this.stickLive = true;
      } else if (this.live && this.stickLive) {
        this.live.scrollTop = this.live.scrollHeight;
      }
    }

    // Composition updates are not text yet; the confirmed text is sent once, whichever of input/compositionend comes last.
    send() {
      const text = this.box.value;
      if (this.composing || text === this.lastSent) return;
      this.lastSent = text;
      LL.send('source', { text, rev: ++LL.state.sentRev });
    }

    onInput = () => {
      autosize(this.box);
      this.send();
    };

    onCompositionStart = () => {
      this.composing = true;
    };

    onCompositionEnd = () => {
      this.composing = false;
      this.send();
    };

    // The composition handlers are lowercase on purpose: Preact only maps onFoo to "foo" when the element has an onfoo
    // property, which composition events lack, so onCompositionStart would listen for an event that never fires.
    render({ source, options }) {
      const live = source.mode === 'live';
      const timed = source.lines.some((line) => line.time);
      const collapsed = source.collapsible && !this.state.expanded;
      return html`<section class=${'pane src' + (collapsed ? ' collapsed' : '')} aria-label=${source.label}>
        <div class="pane-label"><span id="src-label">${source.label}</span>${source.routeChip && html`<span id="route-chip" class="chip mini">${LL.t('routeChip')}</span>`}
          ${source.collapsible && html`<button type="button" class="source-toggle" aria-expanded=${!collapsed} aria-controls="source-content"
            onClick=${() => this.setState({ expanded: !this.state.expanded })}>${LL.t(collapsed ? 'sourceExpand' : 'sourceCollapse')}</button>`}
          <button type="button" id="clear" class="clear-button" disabled=${!source.canClear} title=${LL.t('clearAria')}
            aria-label=${LL.t('clearAria')} onClick=${() => LL.send('newDraft')}>${LL.t('clear')}</button></div>
        <div id="source-content" class="source-content" hidden=${collapsed}>
        <textarea id="src" ref=${this.setBox} rows="2" spellcheck="false" placeholder=${LL.t('placeholder')} aria-label=${source.label}
          hidden=${live} readOnly=${!source.editable} onInput=${this.onInput} oncompositionstart=${this.onCompositionStart}
          oncompositionend=${this.onCompositionEnd}></textarea>
        ${live && html`<div id="src-live" class=${'src-live' + (timed ? '' : ' prose')} ref=${this.setLive} onScroll=${this.onLiveScroll}>${source.lines.map((line, i) => html`<div class=${'line' + (line.tail ? ' tail' : '')} key=${i}>
          ${line.time && html`<time>${line.time}</time>`}<span>${line.text}</span>${line.tail && html`<i class="caret"></i>`}</div>`)}</div>`}
        ${source.hint && html`<p id="hint" class="hint">${LL.t('hintMic')} · <kbd>${options.commitKeyCap}</kbd> ${LL.t('hintWrite')} · <kbd>${options.newlineKeyCap}</kbd> ${LL.t('hintNewline')} · <kbd>Esc</kbd> ${LL.t('hintHide')}</p>`}
        ${source.remembered && html`<p id="remembered" class="remembered"><span>${source.remembered.text}</span>
          <button type="button" id="restore-defaults" class="link" onClick=${() => LL.send('restoreDefaults')}>${source.remembered.action}</button></p>`}
        ${source.resume && html`<button type="button" id="resume" class="resume" onClick=${() => LL.send('restoreRecent', { index: source.resume.index })}>
          <${LL.Icon} name="undo"/><span class="resume-lbl">${LL.t('resume')}</span><span class="resume-txt">${source.resume.label}</span></button>`}
        </div>
      </section>`;
    }
  }

  LL.Source = Source;
})();
