// The controls of the settings page. Each one shows what the snapshot says and reports what the user did; none keeps a
// value of its own except while the user is typing in it.
(function () {
  'use strict';

  const html = LL.html;

  // A row: what the setting is on the left, its control on the right.
  LL.Row = ({ id, title, desc, extra, children }) => html`<div class="srow">
    <div class="stext"><label for=${id}>${title}</label><p>${desc}</p>${extra}</div>
    <div class="sctl">${children}</div>
  </div>`;

  LL.Select = ({ id, value, options, onChange }) => html`<select id=${id} onChange=${(e) => onChange(e.target.value)}>
    ${options.map(([v, label]) => html`<option key=${v} value=${v} selected=${v === value}>${label}</option>`)}
  </select>`;

  // The box always shows the snapshot: a change the host does not accept (a cloud step waiting for consent) must not stay checked.
  LL.Switch = ({ id, label, checked, onChange }) => html`<input type="checkbox" role="switch" id=${id} aria-label=${label} checked=${checked}
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
