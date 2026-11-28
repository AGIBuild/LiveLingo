// The controls of the settings page. Each one shows what the snapshot says and reports what the user did; none keeps a
// value of its own except while the user is typing in it.
(function () {
  'use strict';

  const html = LL.html;

  // A row: what the setting is on the left, its control on the right.
  LL.Row = ({ id, rowId, title, desc, extra, stacked, children }) => html`<div id=${rowId} class=${'srow' + (stacked ? ' stacked' : '')}>
    <div class="stext">${id ? html`<label for=${id}>${title}</label>` : html`<span class="srow-title">${title}</span>`}<p>${desc}</p>${extra}</div>
    <div class="sctl">${children}</div>
  </div>`;

  // A visual group wrapper: subtle section title above a floating white/dark card.
  LL.Group = ({ id, title, action, meta, children }) => html`<section class="sgroup" aria-labelledby=${id}>
    <div class="sgroup-head">
      <h3 id=${id} class="sgroup-title">${title}</h3>
      ${meta && html`<span class="sgroup-meta">${meta}</span>`}
      ${action}
    </div>
    <div class="scard">${children}</div>
  </section>`;

  // Segmented single-select radio control (for 2–3 mutually exclusive choices).
  LL.SegRadio = ({ id, name, label, value, options, onChange }) => html`<div id=${id} class="seg-radio" role="radiogroup" aria-label=${label}>
    ${options.map(([v, text]) => html`<label key=${v} class=${'seg-opt' + (v === value ? ' on' : '')}>
      <input type="radio" name=${name} value=${v} checked=${v === value}
        onChange=${(e) => {
          e.target.checked = v === value;
          if (v !== value) onChange(v);
        }}/>
      <span>${text}</span>
    </label>`)}
  </div>`;

  LL.Select = ({ id, value, options, onChange }) => html`<select id=${id} onChange=${(e) => onChange(e.target.value)}>
    ${options.map(([v, label]) => html`<option key=${v} value=${v} selected=${v === value}>${label}</option>`)}
  </select>`;

  // The box always shows the snapshot: a change the host does not accept (a cloud step waiting for consent) must not stay checked.
  LL.Switch = ({ id, label, checked, disabled, onChange }) => html`<input type="checkbox" role="switch" id=${id} aria-label=${label} checked=${checked} disabled=${disabled}
    onChange=${(e) => {
      e.target.checked = checked;
      onChange(!checked);
    }}/>`;

  const MAC = /Mac/.test(navigator.userAgent);
  const MODIFIERS = { ctrl: 'Ctrl', alt: MAC ? 'Option' : 'Alt', shift: 'Shift', meta: MAC ? 'Cmd' : 'Win' };

  // The keys both platforms can register as a global hotkey. e.code is the physical key, so the layout does not matter.
  function keyName(code) {
    if (/^Key[A-Z]$/.test(code)) return code.slice(3);
    if (/^Digit[0-9]$/.test(code)) return code.slice(5);
    return code === 'Space' ? 'Space' : '';
  }

  const display = (hotkey) => (MAC ? hotkey.replace(/Win\+/gi, 'Cmd+').replace(/Alt\+/gi, 'Option+') : hotkey);

  // Click, then press the combination. The page only reports the chord; C# decides whether it is a usable hotkey.
  class HotkeyField extends preact.Component {
    state = { recording: false };

    // WebKit does not focus a button on click, and the keys must reach this one.
    start = (e) => {
      e.currentTarget.focus();
      this.setState({ recording: true });
    };

    stop = () => this.setState({ recording: false });

    onKeyDown = (e) => {
      // Tab leaves the box (and with it the recording) like anywhere else.
      if (!this.state.recording || e.key === 'Tab') return;
      e.preventDefault();
      if (e.key === 'Escape') {
        this.stop();
        return;
      }
      const key = keyName(e.code);
      const modifiers = [e.ctrlKey && MODIFIERS.ctrl, e.altKey && MODIFIERS.alt, e.shiftKey && MODIFIERS.shift, e.metaKey && MODIFIERS.meta].filter(Boolean);
      // A modifier alone, or a key alone, is not a chord yet: keep waiting.
      if (!key || modifiers.length === 0) return;
      this.stop();
      this.props.onChange([...modifiers, key].join('+'));
    };

    render({ id, value }, { recording }) {
      return html`<button type="button" id=${id} class=${'hotkey' + (recording ? ' recording' : '')}
        onClick=${this.start} onBlur=${this.stop} onKeyDown=${this.onKeyDown}>${recording ? LL.t('hotkey.prompt') : display(value)}</button>`;
    }
  }

  LL.HotkeyField = HotkeyField;
})();
