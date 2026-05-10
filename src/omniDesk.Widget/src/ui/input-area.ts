// Spec 007 — Input box with send button + typing-debounce.
// Send button stays disabled until LGPD consent is granted (parent toggles via setEnabled).

import { allowedAccept } from '../lib/mime-detect';

export interface InputAreaCallbacks {
  onSend: (text: string) => void;
  onTyping: () => void;
  onAttach?: (file: File) => void;
}

export interface InputAreaHandle {
  el: HTMLDivElement;
  setEnabled: (enabled: boolean) => void;
  setPlaceholder: (text: string) => void;
  focus: () => void;
}

export function createInputArea(
  placeholder: string,
  callbacks: InputAreaCallbacks,
): InputAreaHandle {
  const wrapper = document.createElement('div');
  wrapper.className = 'input';

  const textarea = document.createElement('textarea');
  textarea.rows = 1;
  textarea.placeholder = placeholder;
  textarea.setAttribute('aria-label', 'Digite uma mensagem');

  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'send';
  button.textContent = 'Enviar';
  button.disabled = true;

  // Attach button (Spec 007 US6) — only wired when callbacks.onAttach is provided.
  let attachButton: HTMLButtonElement | null = null;
  let fileInput: HTMLInputElement | null = null;
  if (callbacks.onAttach) {
    attachButton = document.createElement('button');
    attachButton.type = 'button';
    attachButton.className = 'attach';
    attachButton.style.cssText = 'background:transparent;border:none;cursor:pointer;font-size:18px;padding:0 6px;';
    attachButton.setAttribute('aria-label', 'Anexar arquivo');
    attachButton.textContent = '📎';
    attachButton.disabled = true;

    fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.accept = allowedAccept;
    fileInput.style.display = 'none';

    attachButton.addEventListener('click', () => fileInput!.click());
    fileInput.addEventListener('change', () => {
      const file = fileInput!.files?.[0];
      if (file) callbacks.onAttach!(file);
      fileInput!.value = ''; // allow re-selecting same file
    });
  }

  wrapper.appendChild(textarea);
  if (attachButton) wrapper.appendChild(attachButton);
  if (fileInput) wrapper.appendChild(fileInput);
  wrapper.appendChild(button);

  let enabled = false;

  function trySend(): void {
    if (!enabled) return;
    const value = textarea.value.trim();
    if (!value) return;
    callbacks.onSend(value);
    textarea.value = '';
    textarea.style.height = 'auto';
  }

  button.addEventListener('click', () => trySend());

  textarea.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      trySend();
    }
  });

  textarea.addEventListener('input', () => {
    textarea.style.height = 'auto';
    textarea.style.height = `${Math.min(textarea.scrollHeight, 120)}px`;
    if (enabled && textarea.value.trim()) callbacks.onTyping();
  });

  return {
    el: wrapper,
    setEnabled(value) {
      enabled = value;
      button.disabled = !value;
      textarea.disabled = !value;
      if (attachButton) attachButton.disabled = !value;
    },
    setPlaceholder(text) { textarea.placeholder = text; },
    focus() { textarea.focus(); },
  };
}
