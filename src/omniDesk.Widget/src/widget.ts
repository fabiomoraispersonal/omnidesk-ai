// Spec 007 — Widget entry point. Defines <omnidesk-widget> custom element with a closed
// shadow root, reads window.OmniDeskConfig, and orchestrates launcher + panel + WS lifecycle.

import type {
  InitResponse,
  Message,
  OmniDeskConfig,
  StartConversationResponse,
  WsEvent,
} from './types';
import { HttpClient } from './api/http-client';
import { WsClient } from './api/ws-client';
import { conversationStore } from './state/conversation-store';
import { visitorStore } from './state/visitor-store';
import { createLauncher } from './ui/launcher';
import { createPanel } from './ui/panel';
import { getStyles } from './ui/styles';
import { generateUuid } from './lib/crypto-uuid';
import { debounce } from './lib/debounce';

declare const __DEFAULT_API_BASE_URL__: string;

declare global {
  interface Window {
    OmniDeskConfig?: OmniDeskConfig;
    OmniDesk?: { open: () => void; close: () => void };
  }
}

class OmniDeskWidget extends HTMLElement {
  private readonly shadow: ShadowRoot;
  private http: HttpClient | null = null;
  private ws: WsClient | null = null;
  private init: InitResponse | null = null;
  private conversationId: string | null = null;
  private hasConsent = false;
  private panel = createPanel({
    onSend: (text) => this.handleSend(text),
    onTyping: () => this.handleTyping(),
    onAttach: (file) => this.handleAttach(file),
    onConsentGranted: () => { this.hasConsent = true; },
    onClose: () => this.panel.setOpen(false),
  });
  private launcher = createLauncher('bottom_right', { onClick: () => this.toggleOpen() });

  constructor() {
    super();
    this.shadow = this.attachShadow({ mode: 'closed' });
  }

  connectedCallback(): void {
    const cfg = window.OmniDeskConfig;
    if (!cfg?.token) {
      console.warn('[OmniDesk] Missing window.OmniDeskConfig.token; widget will not initialize.');
      return;
    }

    const visitorId = visitorStore.getOrCreate();
    const apiBase = cfg.apiBaseUrl ?? __DEFAULT_API_BASE_URL__;
    this.http = new HttpClient(apiBase, cfg.token, visitorId);

    const style = document.createElement('style');
    style.textContent = getStyles('#2563EB');
    this.shadow.appendChild(style);
    this.shadow.appendChild(this.launcher.el);
    this.shadow.appendChild(this.panel.el);

    window.OmniDesk = {
      open: () => this.panel.setOpen(true),
      close: () => this.panel.setOpen(false),
    };
  }

  disconnectedCallback(): void {
    this.ws?.destroy();
    this.ws = null;
  }

  private async toggleOpen(): Promise<void> {
    if (this.panel.isOpen()) {
      this.panel.setOpen(false);
      return;
    }
    if (!this.init) await this.loadInit();
    this.panel.setOpen(true);
  }

  private async loadInit(): Promise<void> {
    if (!this.http) return;
    try {
      const init = await this.http.get<InitResponse>('/api/public/widget/init');
      this.init = init;
      const style = this.shadow.querySelector('style');
      if (style && init.config.primary_color) style.textContent = getStyles(init.config.primary_color);

      // Spec 007 US4 — branch on active_conversation.status.
      const active = init.active_conversation;
      if (active === null || active === undefined) {
        this.hasConsent = false;
        this.panel.hydrate(init, false);
        return;
      }

      switch (active.status) {
        case 'open': {
          this.conversationId = active.id;
          conversationStore.setActive(active.id, 'open');
          this.hasConsent = true;
          this.panel.hydrate(init, true);
          await this.loadHistory();
          await this.connectWs(init);
          break;
        }
        case 'resolved': {
          // Read-only mode: show history + CTA to start a new conversation.
          this.conversationId = active.id;
          conversationStore.setActive(active.id, 'resolved');
          this.hasConsent = true;
          this.panel.hydrate(init, true);
          await this.loadHistory();
          this.panel.setResolvedMode(() => this.startNewConversation());
          break;
        }
        case 'abandoned':
        default: {
          // Wipe stale state and treat as a fresh visit.
          conversationStore.clear();
          this.conversationId = null;
          this.hasConsent = false;
          this.panel.hydrate(init, false);
          break;
        }
      }
    } catch (err) {
      console.warn('[OmniDesk] /init failed', err);
    }
  }

  private startNewConversation(): void {
    conversationStore.clear();
    this.conversationId = null;
    if (this.init) this.panel.hydrate(this.init, this.hasConsent);
  }

  private async loadHistory(): Promise<void> {
    if (!this.http || !this.conversationId) return;
    try {
      const result = await this.http.get<{ messages: Message[] }>(
        `/api/public/widget/conversations/${this.conversationId}/messages`,
        { limit: 50 },
      );
      this.panel.setMessages(result.messages);
      const last = result.messages[result.messages.length - 1];
      if (last) conversationStore.setLastMessageId(last.id);
    } catch (err) {
      console.warn('[OmniDesk] history load failed', err);
    }
  }

  private async handleSend(text: string): Promise<void> {
    if (!this.http) return;
    if (!this.conversationId) {
      // First message — POST /conversations to bootstrap.
      try {
        const visitorId = visitorStore.getOrCreate();
        const start = await this.http.post<StartConversationResponse>('/api/public/widget/conversations', {
          anonymous_id: visitorId,
          lgpd_consent: this.hasConsent,
          metadata: {
            page_url: window.location.href,
            page_title: document.title,
            referrer: document.referrer || undefined,
          },
        });
        this.conversationId = start.conversation_id;
        conversationStore.setActive(start.conversation_id, 'open');
        if (this.init) await this.connectWs(this.init);
      } catch (err) {
        console.warn('[OmniDesk] start conversation failed', err);
        return;
      }
    }

    // Echo locally for snappy UX; the server will echo back via message.new.
    const clientMessageId = generateUuid();
    this.panel.appendMessage({
      id: clientMessageId,
      sender_type: 'visitor',
      content_type: 'text',
      content: text,
      created_at: new Date().toISOString(),
    });
    this.ws?.enqueueMessageSend({ client_message_id: clientMessageId, content: text });
  }

  private readonly handleTyping = debounce(() => {
    this.ws?.send({ type: 'visitor.typing' });
  }, 1_000);

  private async handleAttach(file: File): Promise<void> {
    if (!this.http || !this.conversationId) {
      console.warn('[OmniDesk] attach attempted before conversation started');
      return;
    }
    const cfg = window.OmniDeskConfig;
    if (!cfg) return;
    const apiBase = cfg.apiBaseUrl ?? __DEFAULT_API_BASE_URL__;
    const visitorId = visitorStore.getOrCreate();

    if (file.size > 10 * 1024 * 1024) {
      console.warn('[OmniDesk] file exceeds 10MB; aborting upload.');
      return;
    }

    const form = new FormData();
    form.append('file', file);
    form.append('conversationId', this.conversationId);

    try {
      const response = await fetch(`${apiBase.replace(/\/$/, '')}/api/public/widget/upload`, {
        method: 'POST',
        body: form,
        headers: {
          'X-Widget-Token': cfg.token,
          'X-Anonymous-Id': visitorId,
        },
        credentials: 'omit',
      });
      if (!response.ok) {
        console.warn('[OmniDesk] upload failed', response.status, await response.text());
        return;
      }
      const body = (await response.json()) as {
        success: true;
        data: {
          attachment_url: string;
          attachment_name: string;
          attachment_size_bytes: number;
          mime: string;
          content_type: 'image' | 'file';
        };
      };
      // Echo locally so the visitor sees the attachment immediately. The CRM side
      // gets it via the persisted message + the existing WS pipeline.
      this.panel.appendMessage({
        id: `local-${Date.now()}`,
        sender_type: 'visitor',
        content_type: body.data.content_type,
        content: body.data.attachment_name,
        attachment_url: body.data.attachment_url,
        attachment_name: body.data.attachment_name,
        attachment_size_bytes: body.data.attachment_size_bytes,
        created_at: new Date().toISOString(),
      });
    } catch (err) {
      console.warn('[OmniDesk] upload error', err);
    }
  }

  private async connectWs(init: InitResponse): Promise<void> {
    if (this.ws || !this.conversationId) return;
    const cfg = window.OmniDeskConfig!;
    const apiBase = cfg.apiBaseUrl ?? __DEFAULT_API_BASE_URL__;
    const wsBase = (cfg.wsBaseUrl ?? apiBase).replace(/^http/, 'ws');
    const url = `${wsBase.replace(/\/$/, '')}/ws/widget/${this.conversationId}?token=${encodeURIComponent(cfg.token)}`;

    this.ws = new WsClient({
      url,
      onOpen: () => this.panel.setConnected(true),
      onClose: () => this.panel.setConnected(false),
      getLastMessageId: () => conversationStore.getLastMessageId(),
      onMessage: (event) => this.handleWsEvent(event),
    });
    this.ws.connect();
    void init; // hydrate already consumed init; future enhancements may use it here.
  }

  private handleWsEvent(event: WsEvent): void {
    switch (event.type) {
      case 'message.new':
        this.panel.appendMessage(event.payload);
        conversationStore.setLastMessageId(event.payload.id);
        break;
      case 'agent.typing':
        this.panel.setTyping(true);
        setTimeout(() => this.panel.setTyping(false), 3_000);
        break;
      case 'conversation.resolved':
        this.panel.appendMessage({
          id: `system-resolved-${Date.now()}`,
          sender_type: 'system',
          content_type: 'text',
          content: 'Atendimento encerrado.',
          created_at: new Date().toISOString(),
        });
        conversationStore.clear();
        break;
      default:
        // intentionally unhandled — non-fatal events are no-ops.
        break;
    }
  }
}

if (!customElements.get('omnidesk-widget')) {
  customElements.define('omnidesk-widget', OmniDeskWidget);
}

if (!document.querySelector('omnidesk-widget')) {
  const host = document.createElement('omnidesk-widget');
  document.body.appendChild(host);
}
