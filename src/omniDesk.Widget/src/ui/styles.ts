// Spec 007 — CSS-in-JS template for the widget shadow root. Returns a single string
// suitable for an adopted stylesheet or a <style> element. Color theme derives from the
// tenant's primary_color; everything else is fixed.

export function getStyles(primaryColor: string): string {
  const fg = readableForeground(primaryColor);
  return `
    :host { all: initial; font-family: 'Manrope', 'Inter', system-ui, sans-serif; color: #2F2F2F; }
    *, *::before, *::after { box-sizing: border-box; }

    .launcher {
      position: fixed; bottom: 24px; right: 24px;
      width: 56px; height: 56px; border-radius: 999px;
      background: ${primaryColor}; color: ${fg};
      display: flex; align-items: center; justify-content: center;
      cursor: pointer; box-shadow: 0 8px 24px rgba(0,0,0,0.18);
      border: none; transition: transform 120ms ease;
    }
    .launcher:hover { transform: scale(1.04); }
    .launcher.left { right: auto; left: 24px; }
    .launcher .badge {
      position: absolute; top: -2px; right: -2px;
      min-width: 20px; height: 20px; border-radius: 999px;
      background: #B85C5C; color: white;
      font-size: 11px; font-weight: 700;
      display: none; align-items: center; justify-content: center;
      padding: 0 6px;
    }
    .launcher .badge.visible { display: flex; }

    .panel {
      position: fixed; bottom: 96px; right: 24px;
      width: 360px; max-width: calc(100vw - 32px);
      height: min(560px, calc(100vh - 120px));
      background: #FFFFFF;
      border-radius: 16px; overflow: hidden;
      box-shadow: 0 16px 48px rgba(0,0,0,0.20);
      display: none; flex-direction: column;
      animation: slide-up 220ms ease-out;
    }
    .panel.left { right: auto; left: 24px; }
    .panel.open { display: flex; }
    @keyframes slide-up { from { transform: translateY(20px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }

    .header {
      background: ${primaryColor}; color: ${fg};
      padding: 14px 16px; font-weight: 600;
      display: flex; align-items: center; justify-content: space-between;
    }
    .header button {
      background: transparent; border: none; color: inherit;
      cursor: pointer; font-size: 20px; line-height: 1; padding: 4px;
    }

    .body { flex: 1 1 auto; overflow-y: auto; padding: 12px; background: #F4F1EC; }
    .body .empty { color: #7A7A7A; font-size: 14px; text-align: center; padding: 24px 8px; }

    .msg { display: flex; margin-bottom: 8px; }
    .msg .bubble {
      max-width: 78%; padding: 10px 12px; border-radius: 14px; font-size: 14px;
      white-space: pre-wrap; word-break: break-word;
      box-shadow: 0 1px 2px rgba(0,0,0,0.06);
    }
    .msg.visitor { justify-content: flex-end; }
    .msg.visitor .bubble { background: ${primaryColor}; color: ${fg}; border-bottom-right-radius: 4px; }
    .msg.agent  { justify-content: flex-start; }
    .msg.agent .bubble  { background: white; color: #2F2F2F; border-bottom-left-radius: 4px; }
    .msg.system .bubble { background: transparent; color: #7A7A7A; font-style: italic; font-size: 12px; }
    .msg .ts { font-size: 10px; color: #9A9A9A; margin-top: 2px; text-align: right; }
    .typing { font-size: 12px; color: #7A7A7A; padding: 4px 8px; }

    .lgpd {
      padding: 12px; border-top: 1px solid #EDE7DF; background: white;
      font-size: 13px; color: #2F2F2F;
    }
    .lgpd label { display: flex; gap: 8px; align-items: flex-start; }
    .lgpd a { color: ${primaryColor}; }

    .input {
      display: flex; align-items: flex-end; gap: 8px;
      padding: 10px 12px; border-top: 1px solid #EDE7DF; background: white;
    }
    .input textarea {
      flex: 1 1 auto; resize: none; border: 1px solid #EDE7DF; border-radius: 10px;
      padding: 8px 10px; font-size: 14px; min-height: 40px; max-height: 120px;
      font-family: inherit; outline: none;
    }
    .input textarea:focus { border-color: ${primaryColor}; }
    .input button.send {
      background: ${primaryColor}; color: ${fg}; border: none;
      padding: 0 14px; height: 40px; border-radius: 10px;
      font-weight: 600; cursor: pointer;
    }
    .input button.send:disabled { opacity: 0.45; cursor: not-allowed; }

    .banner {
      background: #C09A4D; color: white; padding: 6px 10px;
      font-size: 12px; text-align: center;
    }
  `;
}

function readableForeground(hex: string): string {
  const c = hex.replace('#', '');
  if (c.length !== 6) return '#FFFFFF';
  const r = parseInt(c.slice(0, 2), 16);
  const g = parseInt(c.slice(2, 4), 16);
  const b = parseInt(c.slice(4, 6), 16);
  // Relative luminance per WCAG 2.x (approximation).
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.6 ? '#2F2F2F' : '#FFFFFF';
}
