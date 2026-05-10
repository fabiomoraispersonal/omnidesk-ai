// Spec 007 — Panel orchestrator. Coordinates header, message list, optional LGPD block,
// input area, and the disconnection banner. Lazy-fetches /init on first open.

import type { InitResponse, Message, WidgetConfig, ActiveConversation } from '../types';
import { createMessageList, type MessageListHandle } from './message-list';
import { createInputArea, type InputAreaHandle } from './input-area';
import { createLgpdConsent } from './lgpd-consent';

export interface PanelCallbacks {
  onSend: (text: string) => void;
  onTyping: () => void;
  onConsentGranted: () => void;
  onClose: () => void;
  onAttach?: (file: File) => void;
}

export interface PanelHandle {
  el: HTMLDivElement;
  setOpen: (open: boolean) => void;
  isOpen: () => boolean;
  applyConfig: (cfg: WidgetConfig & { company_name?: string }) => void;
  hydrate: (init: InitResponse, hasConsent: boolean) => void;
  setConsentGiven: () => void;
  setMessages: (messages: Message[]) => void;
  appendMessage: (message: Message) => void;
  setTyping: (active: boolean) => void;
  setConnected: (connected: boolean) => void;
  // Spec 007 US4 — switches the panel into read-only history mode with a "start new" CTA.
  setResolvedMode: (onStartNew: () => void) => void;
}

export function createPanel(callbacks: PanelCallbacks): PanelHandle {
  const root = document.createElement('div');
  root.className = 'panel';

  const header = document.createElement('div');
  header.className = 'header';
  header.innerHTML = `<span class="title">Atendimento</span>
                      <button type="button" aria-label="Fechar">×</button>`;
  (header.querySelector('button') as HTMLButtonElement).addEventListener('click', callbacks.onClose);

  const banner = document.createElement('div');
  banner.className = 'banner';
  banner.style.display = 'none';
  banner.textContent = 'Reconectando…';

  const messages: MessageListHandle = createMessageList('Olá! Como posso ajudar?');

  const inputArea: InputAreaHandle = createInputArea('Digite uma mensagem…', {
    onSend: callbacks.onSend,
    onTyping: callbacks.onTyping,
    ...(callbacks.onAttach ? { onAttach: callbacks.onAttach } : {}),
  });

  let lgpdSlot: HTMLElement | null = null;

  root.appendChild(header);
  root.appendChild(banner);
  root.appendChild(messages.el);
  root.appendChild(inputArea.el);

  return {
    el: root,
    setOpen(open) {
      if (open) root.classList.add('open');
      else root.classList.remove('open');
      if (open) inputArea.focus();
    },
    isOpen() { return root.classList.contains('open'); },
    applyConfig(cfg) {
      const titleSpan = header.querySelector('.title') as HTMLSpanElement;
      titleSpan.textContent = cfg.company_name ?? 'Atendimento';
      if (cfg.position === 'bottom_left') root.classList.add('left');
      else root.classList.remove('left');
      if (cfg.input_placeholder) inputArea.setPlaceholder(cfg.input_placeholder);
    },
    hydrate(init, hasConsent) {
      const cfg = init.config;
      this.applyConfig({ ...cfg, company_name: init.tenant.company_name });

      // Disabled state: short-circuit the panel — show only the disabled message.
      if (init.config.is_enabled === false || init.disabled_message) {
        messages.setMessages([{
          id: 'system-disabled',
          sender_type: 'system',
          content_type: 'text',
          content: init.disabled_message ?? 'Atendimento indisponível.',
          created_at: new Date().toISOString(),
        }]);
        inputArea.setEnabled(false);
        return;
      }

      messages.setMessages(welcomeAsMessages(init.config, init.active_conversation));

      if (!hasConsent) {
        // Inject LGPD slot above the input until consent is granted.
        lgpdSlot = createLgpdConsent(
          { text: cfg.privacy_policy_text ?? null, url: cfg.privacy_policy_url ?? null },
          () => {
            callbacks.onConsentGranted();
            this.setConsentGiven();
          },
        );
        root.insertBefore(lgpdSlot, inputArea.el);
        inputArea.setEnabled(false);
      } else {
        inputArea.setEnabled(true);
      }
    },
    setConsentGiven() {
      if (lgpdSlot && lgpdSlot.parentNode) lgpdSlot.parentNode.removeChild(lgpdSlot);
      lgpdSlot = null;
      inputArea.setEnabled(true);
    },
    setMessages: (m) => messages.setMessages(m),
    appendMessage: (m) => messages.appendMessage(m),
    setTyping: (a) => messages.setTyping(a),
    setConnected(connected) {
      banner.style.display = connected ? 'none' : 'block';
    },
    setResolvedMode(onStartNew) {
      inputArea.setEnabled(false);
      banner.style.display = 'block';
      banner.textContent = 'Conversa encerrada.';

      // Append a CTA below the message list (idempotent — replaces any existing one).
      const existing = root.querySelector('.cta-start-new');
      if (existing) existing.remove();
      const cta = document.createElement('button');
      cta.type = 'button';
      cta.className = 'cta-start-new send';
      cta.style.margin = '8px 12px';
      cta.textContent = 'Iniciar nova conversa';
      cta.addEventListener('click', () => {
        cta.remove();
        banner.style.display = 'none';
        onStartNew();
      });
      root.insertBefore(cta, inputArea.el);
    },
  };
}

function welcomeAsMessages(cfg: WidgetConfig, active: ActiveConversation | null): Message[] {
  if (active) return [];
  const text = cfg.welcome_message ?? 'Olá! Como posso ajudar?';
  return [{
    id: 'welcome',
    sender_type: 'ai_agent',
    content_type: 'text',
    content: text,
    created_at: new Date().toISOString(),
  }];
}
