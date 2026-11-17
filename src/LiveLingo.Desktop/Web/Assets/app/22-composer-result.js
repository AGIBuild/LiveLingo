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

  const Document = ({ result, options }) => {
    const doc = result.document;
    return html`<div class="document-body">
      ${doc.editing
        ? html`<${EditRow} key="document-editor" row=${{ editAria: LL.t('documentEdit') }} text=${result.editText} options=${options} document=${true}/>`
        : html`<p id="final-text" class="document-text">${doc.text}</p>
            <p class=${'document-notice' + (doc.needsChoice ? ' warn' : '')} role="status">${doc.notice}</p>
            <div class="document-actions">
              <button type="button" id="edit-full" disabled=${!doc.canEdit} onClick=${() => LL.send('editDocument')}>${LL.t('documentEdit')}</button>
              ${(doc.needsChoice || doc.owned) && html`<button type="button" disabled=${!doc.canEdit} onClick=${() => LL.send('retryDocument')}>${LL.t('documentRetry')}</button>`}
              ${doc.needsChoice && html`${doc.canAcceptPreview && html`<button type="button" onClick=${() => LL.send('acceptPreview')}>${LL.t('documentAccept')}</button>`}
                ${doc.canClean && html`<button type="button" id="clean-original" disabled=${!doc.canEdit} onClick=${() => LL.send('output', { value: 'clean' })}>${LL.t('documentClean')}</button>`}
                <button type="button" disabled=${!doc.canEdit} onClick=${() => LL.send('output', { value: 'original' })}>${LL.t('documentOriginal')}</button>`}
            </div>`}
    </div>`;
  };

  LL.Result = ({ result, options }) => {
    if (!result.visible) return null;
    return html`<section id="res" class="pane res" aria-label=${LL.t('resultAria')}>
      <div class="pane-label"><span id="res-label">${result.label}</span></div>
      ${result.document ? html`<${Document} result=${result} options=${options}/>` : html`<div id="rows" class="rows">${result.rows.map((row) => html`<article key=${row.i} class=${'unit' + (row.warn ? ' warn' : '') + (row.owned ? ' owned' : '')}>
        ${row.editing ? html`<${EditRow} row=${row} text=${result.editText} options=${options} rememberCorrection=${result.rememberCorrection}/>` : html`<${Body} row=${row}/>`}
        ${row.canEdit && !row.editing && html`<${Tools} row=${row} canUseOriginal=${row.canUseOriginal}/>`}
      </article>`)}</div>`}
    </section>`;
  };
})();
