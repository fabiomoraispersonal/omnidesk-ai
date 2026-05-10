// Spec 007 — Renders messages with auto-scroll, sender alignment, typing indicator
// and an empty-state welcome message.

import type { Message } from '../types';

export interface MessageListHandle {
  el: HTMLDivElement;
  setMessages: (messages: Message[]) => void;
  appendMessage: (message: Message) => void;
  setTyping: (active: boolean) => void;
}

export function createMessageList(welcomeMessage: string): MessageListHandle {
  const container = document.createElement('div');
  container.className = 'body';
  container.setAttribute('aria-live', 'polite');

  const empty = document.createElement('div');
  empty.className = 'empty';
  empty.textContent = welcomeMessage;
  container.appendChild(empty);

  const typing = document.createElement('div');
  typing.className = 'typing';
  typing.style.display = 'none';
  typing.textContent = 'digitando…';

  let messages: Message[] = [];

  function render(): void {
    container.innerHTML = '';
    if (messages.length === 0) {
      const e = empty.cloneNode(true) as HTMLElement;
      container.appendChild(e);
    } else {
      for (const m of messages) container.appendChild(renderOne(m));
    }
    container.appendChild(typing);
    requestAnimationFrame(() => { container.scrollTop = container.scrollHeight; });
  }

  return {
    el: container,
    setMessages(next) { messages = next.slice(); render(); },
    appendMessage(m) {
      // Skip system_event noise from the visible feed (FR-045).
      if (m.content_type === 'system_event') return;
      // Dedupe by id in case a replay race delivers the same message twice.
      if (messages.some((x) => x.id === m.id)) return;
      messages.push(m);
      render();
    },
    setTyping(active) {
      typing.style.display = active ? 'block' : 'none';
      requestAnimationFrame(() => { container.scrollTop = container.scrollHeight; });
    },
  };
}

function renderOne(m: Message): HTMLDivElement {
  const row = document.createElement('div');
  const klass = m.sender_type === 'visitor'
    ? 'msg visitor'
    : m.sender_type === 'system' || m.content_type === 'system_event'
      ? 'msg system'
      : 'msg agent';
  row.className = klass;

  const bubble = document.createElement('div');
  bubble.className = 'bubble';

  if (m.content_type === 'image' && m.attachment_url) {
    const img = document.createElement('img');
    img.src = m.attachment_url;
    img.alt = m.attachment_name ?? 'imagem';
    img.style.maxWidth = '100%';
    img.style.borderRadius = '8px';
    bubble.appendChild(img);
  } else if (m.content_type === 'file' && m.attachment_url) {
    const link = document.createElement('a');
    link.href = m.attachment_url;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    link.textContent = m.attachment_name ?? 'arquivo';
    bubble.appendChild(link);
  } else {
    bubble.textContent = m.content ?? '';
  }

  row.appendChild(bubble);
  return row;
}
