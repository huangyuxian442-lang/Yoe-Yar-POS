window.pos = {
  focus(id) {
    const el = document.getElementById(id);
    if (el) { el.focus(); if (el.select) el.select(); }
  },
  print() { window.print(); },
  // F2 = search, F4 = PCS/CTN, F9 = pay, Esc = close dialogs. Forwarded to the checkout page.
  _handler: null,
  registerHotkeys(dotnet) {
    this.unregisterHotkeys();
    this._handler = e => {
      if (['F2', 'F4', 'F9', 'Escape'].includes(e.key)) {
        e.preventDefault();
        dotnet.invokeMethodAsync('OnHotkey', e.key);
      }
    };
    document.addEventListener('keydown', this._handler);
  },
  unregisterHotkeys() {
    if (this._handler) document.removeEventListener('keydown', this._handler);
    this._handler = null;
  }
};
