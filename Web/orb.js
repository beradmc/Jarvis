// ═══════════════════════════════════════════════════════════════
// Jarvis Orb — Floating glass sphere
// Click to summon the overlay. Drag to reposition.
// ═══════════════════════════════════════════════════════════════

const Orb = {
  _ready: false,
  _pendingSummon: false,
  _mouseDownPos: null,
  _dragStarted: false,

  init() {
    const compact = document.getElementById('orb-compact');

    // ── Click to summon overlay, drag to move ───────────────────
    compact.addEventListener('mousedown', (e) => {
      this._mouseDownPos = { x: e.screenX, y: e.screenY };
      this._dragStarted = false;
      this._post({ action: 'orb.dragStart' });
    });

    compact.addEventListener('mousemove', (e) => {
      if (this._mouseDownPos) {
        const dx = Math.abs(e.screenX - this._mouseDownPos.x);
        const dy = Math.abs(e.screenY - this._mouseDownPos.y);
        if (dx > 5 || dy > 5) this._dragStarted = true;
      }
    });

    compact.addEventListener('mouseup', (e) => {
      this._post({ action: 'orb.dragEnd' });
      if (this._mouseDownPos && !this._dragStarted) {
        const dx = Math.abs(e.screenX - this._mouseDownPos.x);
        const dy = Math.abs(e.screenY - this._mouseDownPos.y);
        if (dx < 5 && dy < 5) {
          // Click — summon the overlay via C#
          this._post({ action: 'orb.expand' });
        }
      }
      this._mouseDownPos = null;
    });

    // ── Ready ───────────────────────────────────────────────────
    this._ready = true;
    this._post({ action: 'orb.ready' });
    if (this._pendingSummon) {
      this._pendingSummon = false;
      this.summon();
    }
  },

  summon() {
    if (!this._ready) { this._pendingSummon = true; return; }
    const compact = document.getElementById('orb-compact');
    compact.classList.remove('dismissing');
    compact.classList.add('summoned');
  },

  dismiss() {
    const compact = document.getElementById('orb-compact');
    compact.classList.add('dismissing');
    compact.classList.remove('summoned');
    setTimeout(() => this._post({ action: 'orb.dismiss' }), 300);
  },

  // ── Bridge ────────────────────────────────────────────────────
  _post(msg) {
    try { window.chrome.webview.postMessage(JSON.stringify(msg)); }
    catch (e) { console.warn('bridge not ready', e); }
  },

  // ── Incoming events from C# ────────────────────────────────
  onEvent(event, data) {
    switch (event) {
      case 'summon': this.summon(); break;
      case 'dismiss': this.dismiss(); break;
      case 'response': break; // handled by overlay
      case 'state': break;
    }
  },
};

window.Orb = Orb;
document.addEventListener('DOMContentLoaded', () => Orb.init());
