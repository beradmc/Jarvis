// ═══════════════════════════════════════════════════════════════
// Jarvis Overlay — Siri-style assistant
// Centered input bar + conversation, transparent background
// ═══════════════════════════════════════════════════════════════

// ── Bridge: communicate with C# backend ───────────────────────
const Bridge = {
  _pending: new Map(),
  _handlers: new Map(),

  async call(action, payload = {}) {
    const id = `rpc_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
    return new Promise((resolve, reject) => {
      this._pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage(JSON.stringify({ id, action, ...payload }));
      setTimeout(() => {
        if (this._pending.has(id)) {
          this._pending.get(id).reject(new Error('Request timeout'));
          this._pending.delete(id);
        }
      }, 30000);
    });
  },

  on(event, handler) { this._handlers.set(event, handler); },

  _receive(json) {
    const msg = JSON.parse(json);
    if (msg.id && this._pending.has(msg.id)) {
      const { resolve, reject } = this._pending.get(msg.id);
      this._pending.delete(msg.id);
      msg.ok ? resolve(msg.data) : reject(new Error(msg.error || 'Unknown error'));
    } else if (msg.event && this._handlers.has(msg.event)) {
      this._handlers.get(msg.event)(msg);
    }
  },
};
window.Bridge = Bridge;

// ── Overlay App ───────────────────────────────────────────────
const Overlay = {
  _inputMode: 'text',
  _hasConversation: false,

  init() {
    const input = document.getElementById('chat-input');
    const sendBtn = document.getElementById('btn-send');
    const micBtn = document.getElementById('btn-mic');
    const sparkBtn = document.getElementById('btn-sparkle');

    // Focus input on open
    setTimeout(() => input?.focus(), 100);

    // Send on Enter
    input?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') { e.preventDefault(); this.send(); }
      if (e.key === 'Escape') { this.dismiss(); }
    });

    // Send button
    sendBtn?.addEventListener('click', () => this.send());

    // Voice toggle
    micBtn?.addEventListener('click', () => this.toggleMode());

    // Sparkle (placeholder)
    sparkBtn?.addEventListener('click', () => {
      this._showWelcome();
    });

    // Quick toggles
    document.querySelectorAll('.toggle-btn').forEach(btn => {
      btn.addEventListener('click', () => this.handleToggle(btn.dataset.action));
    });

    // Initial welcome
    this._showWelcome();
  },

  toggleMode() {
    this._inputMode = this._inputMode === 'text' ? 'voice' : 'text';
    const micBtn = document.getElementById('btn-mic');
    const input = document.getElementById('chat-input');

    if (this._inputMode === 'voice') {
      micBtn?.classList.add('voice-active');
      if (input) {
        input.placeholder = 'Listening — speak to Jarvis...';
        input.disabled = true;
      }
      Bridge.call('voice.start');
    } else {
      micBtn?.classList.remove('voice-active');
      if (input) {
        input.placeholder = 'Ask Jarvis anything...';
        input.disabled = false;
        input.focus();
      }
      Bridge.call('voice.stop');
    }
  },

  async send() {
    const input = document.getElementById('chat-input');
    if (!input) return;
    const text = input.value.trim();
    if (!text) return;

    // Hide welcome and toggles on first message
    if (!this._hasConversation) {
      this._hasConversation = true;
      const toggles = document.getElementById('quick-toggles');
      if (toggles) toggles.style.display = 'none';
    }

    this._addUserMessage(text);
    input.value = '';

    // Typing indicator
    const typing = this._addTyping();

    try {
      const response = await this._getResponse(text);
      typing.remove();
      this._addJarvisMessage(response);
    } catch (err) {
      typing.remove();
      this._addJarvisMessage(`Error: ${err.message}`);
    }
  },

  async _getResponse(text) {
    const lower = text.toLowerCase();
    if (lower.startsWith('run ') || lower.startsWith('ps ')) {
      const cmd = text.substring(text.indexOf(' ') + 1);
      const result = await Bridge.call('sys.powershell', { command: cmd });
      let out = result.stdout || '';
      if (result.stderr) out += (out ? '\n' : '') + result.stderr;
      return `Exit code: ${result.exitCode}\n\n${out.trim() || '(no output)'}`;
    }
    if (lower.startsWith('cmd ')) {
      const cmd = text.substring(4);
      const result = await Bridge.call('sys.cmd', { command: cmd });
      let out = result.stdout || '';
      if (result.stderr) out += (out ? '\n' : '') + result.stderr;
      return `Exit code: ${result.exitCode}\n\n${out.trim() || '(no output)'}`;
    }
    if (lower.startsWith('open ')) {
      const app = text.substring(5);
      try { await Bridge.call('shell.launchApp', { name: app }); }
      catch { /* non-fatal */ }
      return `Opening ${app}...`;
    }
    return `You said: "${text}".\n\nAI backend (Hermes) not yet connected.`;
  },

  handleToggle(action) {
    const map = {
      wifi: () => Bridge.call('sys.toggleWifi'),
      bluetooth: () => Bridge.call('sys.toggleBluetooth'),
      airplane: () => Bridge.call('sys.toggleAirplane'),
      dnd: () => Bridge.call('sys.toggleDND'),
      flashlight: () => Bridge.call('sys.toggleFlashlight'),
      calculator: () => Bridge.call('shell.launchApp', { name: 'Calculator' }),
    };
    if (map[action]) {
      map[action]().catch(() => {});
      // Visual feedback
      event.target.closest('.toggle-btn')?.classList.toggle('active');
    }
  },

  dismiss() {
    // Tell C# to hide the overlay
    try { window.chrome.webview.postMessage(JSON.stringify({ action: 'overlay.dismiss' })); }
    catch (e) { console.warn('dismiss failed', e); }
  },

  // ── UI helpers ──────────────────────────────────────────────

  _showWelcome() {
    const conv = document.getElementById('conversation');
    if (!conv) return;
    conv.innerHTML = '';
    this._hasConversation = false;
    const toggles = document.getElementById('quick-toggles');
    if (toggles) toggles.style.display = 'flex';
  },

  _addUserMessage(text) {
    const conv = document.getElementById('conversation');
    if (!conv) return;
    const el = document.createElement('div');
    el.className = 'msg user';
    el.textContent = text;
    conv.appendChild(el);
    conv.scrollTop = conv.scrollHeight;
  },

  _addJarvisMessage(text) {
    const conv = document.getElementById('conversation');
    if (!conv) return;
    const el = document.createElement('div');
    el.className = 'msg jarvis';
    // Simple markdown-ish formatting
    el.innerHTML = text
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/`(.+?)`/g, '<code style="background:var(--bg-elevated);padding:2px 6px;border-radius:4px;font-family:monospace;font-size:13px;">$1</code>')
      .replace(/\n/g, '<br>');
    conv.appendChild(el);
    conv.scrollTop = conv.scrollHeight;
  },

  _addTyping() {
    const conv = document.getElementById('conversation');
    if (!conv) return document.createElement('div');
    const el = document.createElement('div');
    el.className = 'typing-indicator';
    el.innerHTML = '<div class="typing-dots"><span></span><span></span><span></span></div>';
    conv.appendChild(el);
    conv.scrollTop = conv.scrollHeight;
    return el;
  },
};

// ── Init ──────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => Overlay.init());
