// Spec 007 — Input box with send button + typing-debounce.
// Send button stays disabled until LGPD consent is granted (parent toggles via setEnabled).

export interface InputAreaCallbacks {
  onSend: (text: string) => void;
  onTyping: () => void;
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

  wrapper.appendChild(textarea);
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
    },
    setPlaceholder(text) { textarea.placeholder = text; },
    focus() { textarea.focus(); },
  };
}
