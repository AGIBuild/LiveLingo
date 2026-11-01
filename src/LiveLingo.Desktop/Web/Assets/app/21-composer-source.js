// The source pane. The text box belongs to the page while the user types (design 5.3): a snapshot replaces its text only
// when C# changed the text behind the page's back (speech, import, a restored draft), never as an echo of typing.
(function () {
  'use strict';

  const html = LL.html;

  function autosize(el) {
    if (el.hidden) return;
    el.style.height = 'auto';
    el.style.height = el.scrollHeight + 'px';
  }

  LL.autosize = autosize;

  class Source extends preact.Component {
    composing = false;
    lastSent = '';

    setBox = (el) => {
      this.box = el;
    };

    setLive = (el) => {
      this.live = el;
    };

    componentDidMount() {
      this.sync();
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
      if (this.live) this.live.scrollTop = this.live.scrollHeight;
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
      return html`<section class="pane src" aria-label=${source.label}>
        <div class="pane-label"><span id="src-label">${source.label}</span>${source.routeChip && html`<span id="route-chip" class="chip mini">${LL.t('routeChip')}</span>`}</div>
        <textarea id="src" ref=${this.setBox} rows="2" spellcheck="false" placeholder=${LL.t('placeholder')} aria-label=${source.label}
          hidden=${live} readOnly=${!source.editable} onInput=${this.onInput} oncompositionstart=${this.onCompositionStart}
          oncompositionend=${this.onCompositionEnd}></textarea>
        ${live && html`<div id="src-live" class="src-live" ref=${this.setLive}>${source.lines.map((line, i) => html`<div class=${'line' + (line.tail ? ' tail' : '')} key=${i}>
          ${line.time && html`<time>${line.time}</time>`}<span>${line.text}</span>${line.tail && html`<i class="caret"></i>`}</div>`)}</div>`}
        ${source.hint && html`<p id="hint" class="hint">${LL.t('hintMic')} · <kbd>${options.commitKeyCap}</kbd> ${LL.t('hintWrite')} · <kbd>${options.newlineKeyCap}</kbd> ${LL.t('hintNewline')} · <kbd>Esc</kbd> ${LL.t('hintHide')}</p>`}
        ${source.resume && html`<button type="button" id="resume" class="resume" onClick=${() => LL.send('restoreRecent', { index: source.resume.index })}>
          <${LL.Icon} name="undo"/><span class="resume-lbl">${LL.t('resume')}</span><span class="resume-txt">${source.resume.label}</span></button>`}
      </section>`;
    }
  }

  LL.Source = Source;
})();
