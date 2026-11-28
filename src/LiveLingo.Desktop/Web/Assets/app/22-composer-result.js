// Document finals and timed passage previews share the same result pane.
(function () {
  'use strict';

  const html = LL.html;
  const Icon = LL.Icon;

  // A sentence editor owns its text while it is open; C# only hands it the starting text, once.
  class EditRow extends preact.Component {
    composing = false;

    setBox = (el) => {
      this.box = el;
    };

    componentDidMount() {
      const box = this.box;
      box.value = this.props.text;
      box.focus();
      box.setSelectionRange(box.value.length, box.value.length);
    }

    send() {
      if (!this.composing) LL.send('editText', { text: this.box.value });
    }

    onInput = () => this.send();

    onCompositionStart = () => {
      this.composing = true;
    };

    onCompositionEnd = () => {
      this.composing = false;
      this.send();
    };

    render({ row, options, rememberCorrection, document }) {
      const id = document ? 'edit-document' : 'edit-' + row.i;
      return html`<label class="sr-only" for=${id}>${row.editAria}</label>
        <textarea class=${'edit' + (document ? ' document-edit' : '')} id=${id} rows=${document ? 10 : 3} ref=${this.setBox} onInput=${this.onInput} oncompositionstart=${this.onCompositionStart}
          oncompositionend=${this.onCompositionEnd}></textarea>
        ${document && html`<p class="edit-hint">${LL.t('documentSaveHint')}</p>`}
        <div class="edit-actions">
          <button type="button" onClick=${() => LL.send('saveEdit')}>${LL.t('rowSave')} <kbd>${document ? (options.modifier === 'meta' ? '⌘↵' : 'Ctrl+↵') : options.commitKeyCap}</kbd></button>
          ${rememberCorrection && html`<button type="button" class="remember-corr" onClick=${() => LL.send('saveEditAndRemember')}>${rememberCorrection.label}</button>`}
          <button type="button" onClick=${() => LL.send('cancelEdit')}>${LL.t('rowCancel')} <kbd>Esc</kbd></button>
        </div>`;
    }
  }

  const Body = ({ row }) => {
    switch (row.state) {
      case 'pending':
        return html`<div class="skel" aria-hidden="true"></div><div class="skel short" aria-hidden="true"></div><span class="sr-only">${LL.t('rowProcessing')}</span>`;
      case 'failed':
        return html`<p class="row-error">${row.text}</p>
          <div class="inline-actions">
            <button type="button" onClick=${() => LL.send('retry', { i: row.i })}>${LL.t('rowRetry')}</button>
            <button type="button" onClick=${() => LL.send('edit', { i: row.i })}>${LL.t('rowManual')}</button>
          </div>`;
      case 'empty':
        return html`<p class="row-empty">${row.text}</p>`;
      default:
        return html`<p class="row-text">${row.text}</p>${row.note && html`<p class=${'row-note' + (row.warn ? '' : ' owned')}>${row.note}</p>`}`;
    }
  };

  const Tools = ({ row, canUseOriginal }) => html`<div class="row-tools">
    ${canUseOriginal && html`<button type="button" class="icon-btn" aria-label=${row.useOriginalAria} title=${LL.t('rowUseOriginal')}
      onClick=${() => LL.send('useOriginal', { i: row.i })}><${Icon} name="undo"/></button>`}
    <button type="button" class="icon-btn" aria-label=${row.editAria} title=${LL.t('rowEdit')}
      onClick=${() => LL.send('edit', { i: row.i })}><${Icon} name="edit"/></button>
  </div>`;

  // Non-destructive syntax highlighting via VNodes: every character of the plain text stays intact.
  const renderInline = (line) => {
    const parts = line.split(/(`[^`\n]+`)/g);
    return parts.map((part, idx) => (part.length >= 2 && part.startsWith('`') && part.endsWith('`')
      ? html`<code class="md-code" key=${idx}>${part}</code>`
      : part));
  };

  const renderStructuredText = (text) => {
    if (!text) return text;
    const lines = text.split('\n');
    const out = [];
    for (let i = 0; i < lines.length; i++) {
      if (i > 0) out.push('\n');
      const line = lines[i];
      if (/^#{2,3}\s+/.test(line)) {
        out.push(html`<span class="md-h" key=${'l' + i}>${line}</span>`);
      } else if (/^-\s+\[[ xX]\]\s+/.test(line)) {
        const prefix = line.slice(0, 6);
        out.push(html`<span key=${'l' + i}><span class="md-cb">${prefix}</span>${renderInline(line.slice(6))}</span>`);
      } else {
        out.push(html`<span key=${'l' + i}>${renderInline(line)}</span>`);
      }
    }
    return out;
  };

  class Document extends preact.Component {
    stickBottom = true;

    setBody = (el) => {
      this.body = el;
    };

    onBodyScroll = () => {
      const el = this.body;
      if (!el) return;
      this.stickBottom = el.scrollHeight - el.scrollTop - el.clientHeight <= 32;
    };

    componentDidUpdate() {
      const doc = this.props.result?.document;
      if (!doc?.streaming) {
        this.stickBottom = true;
        return;
      }
      if (this.body && this.stickBottom) {
        this.body.scrollTop = this.body.scrollHeight;
      }
    }

    render({ result, options }) {
      const doc = result.document;
      const pending = doc.state === 'pending';
      const streaming = pending && !!doc.streaming;
      const textClass = 'document-text' + (streaming ? ' streaming' : pending ? ' previewing' : '');
      return html`<div class="document-body" ref=${this.setBody} onScroll=${this.onBodyScroll} aria-busy=${pending ? 'true' : undefined}>
        ${doc.editing
          ? html`<${EditRow} key="document-editor" row=${{ editAria: LL.t('documentEdit') }} text=${result.editText} options=${options} document=${true}/>`
          : html`${pending && !streaming && html`<div class="doc-generating" aria-hidden="true"><div class="skel"></div><div class="skel short"></div></div>`}
              <p id="final-text" class=${textClass}>${renderStructuredText(doc.text)}${streaming && html`<span class="stream-caret" aria-hidden="true"></span>`}</p>
              <p class=${'document-notice' + ((doc.needsChoice || doc.hasWarning) ? ' warn' : '')} role="status">${doc.notice}</p>
              <div class="document-actions">
                <button type="button" id="edit-full" disabled=${!doc.canEdit || pending} onClick=${() => LL.send('editDocument')}>${LL.t('documentEdit')}</button>
                ${(doc.needsChoice || doc.owned || doc.hasWarning) && html`<button type="button" disabled=${!doc.canEdit} onClick=${() => LL.send('retryDocument')}>${LL.t('documentRetry')}</button>`}
                ${doc.needsChoice && html`${doc.canAcceptPreview && html`<button type="button" onClick=${() => LL.send('acceptPreview')}>${LL.t('documentAccept')}</button>`}
                  ${doc.canClean && html`<button type="button" id="clean-original" disabled=${!doc.canEdit} onClick=${() => LL.send('output', { value: 'clean' })}>${LL.t('documentClean')}</button>`}`}
                ${(doc.needsChoice || doc.hasWarning) && html`<button type="button" disabled=${!doc.canEdit} onClick=${() => LL.send('output', { value: 'original' })}>${LL.t('documentOriginal')}</button>`}
              </div>`}
      </div>`;
    }
  }

  LL.Result = ({ result, options }) => {
    if (!result.visible) return null;
    const gateBadge = result.document?.gateBadge;
    const gateTone = result.document?.gateTone;
    const chipTone = gateTone === 'warn' ? 'warn' : gateTone === 'info' ? 'info' : 'ok';
    return html`<section id="res" class="pane res" aria-label=${LL.t('resultAria')}>
      <div class="pane-label">
        <span id="res-label">${result.label}</span>
        ${gateBadge && html`<span id="gate-badge" class=${'chip mini gate-chip ' + chipTone}>${gateBadge}</span>`}
      </div>
      ${result.document ? html`<${Document} result=${result} options=${options}/>` : html`<div id="rows" class="rows">${result.rows.map((row) => html`<article key=${row.i} class=${'unit' + (row.warn ? ' warn' : '') + (row.owned ? ' owned' : '')}>
        ${row.editing ? html`<${EditRow} row=${row} text=${result.editText} options=${options} rememberCorrection=${result.rememberCorrection}/>` : html`<${Body} row=${row}/>`}
        ${row.canEdit && !row.editing && html`<${Tools} row=${row} canUseOriginal=${row.canUseOriginal}/>`}
      </article>`)}</div>`}
    </section>`;
  };
})();
