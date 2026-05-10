# Contract — Widget Installation

Snippet HTML que o tenant cola no site, parâmetros aceitos e comportamento de inicialização.

---

## Snippet padrão

```html
<script>
  window.OmniDeskConfig = { token: "WIDGET_TOKEN_UUID" };
</script>
<script src="https://cdn.omnicare.ia.br/widget/v1/loader.js" async></script>
```

- `loader.js` é um stub minúsculo (~1 KB) que injeta um `<script type="module">` apontando para `widget.<hash>.js` (versionado).
- O bundle principal (`widget.<hash>.js`) carrega de forma assíncrona — não bloqueia renderização da página host.
- A primeira chamada útil acontece quando o usuário interage (lazy-init): nem o WebSocket nem o GET `/init` são abertos antes do clique no launcher.

> Variável de ambiente do backend `WIDGET_CDN_BASE_URL` controla o domínio (default `https://cdn.omnicare.ia.br/widget/v1`). Para dev: `http://localhost:5173/widget/v1` (servidor esbuild local).

---

## Parâmetros aceitos em `window.OmniDeskConfig`

```ts
interface OmniDeskConfig {
  token: string;            // OBRIGATÓRIO. widget_token UUID público do tenant.
  apiBaseUrl?: string;      // OPCIONAL. Default: extraído do CDN URL → 'https://api.omnicare.ia.br'.
  locale?: 'pt-BR';         // OPCIONAL. V1 só suporta pt-BR; arquitetura preparada para i18n.
  hideLauncher?: boolean;   // OPCIONAL. Default: false. Útil quando o tenant usa botão próprio para abrir o widget.
  initialPosition?: { bottom?: string; left?: string; right?: string }; // OPCIONAL. Override inline (raro).
  user?: {                  // OPCIONAL. Pré-preenchimento de identificação se já conhecida pelo tenant.
    name?: string;
    email?: string;
    phone?: string;
  };
}
```

> O `token` é o **único** campo obrigatório. Demais campos têm defaults pelos quais 99% dos tenants não precisam mexer.

---

## API JS pública (V1.1+)

Reservada para integração avançada (ex.: tenant abre widget via botão próprio). V1 expõe apenas:

```ts
window.OmniDesk = {
  /** Abre o painel programaticamente. No-op se widget desabilitado. */
  open: () => void;
  /** Fecha o painel. */
  close: () => void;
  /** Pré-popula identificação do usuário antes de abrir. */
  setUser: (user: { name?: string; email?: string; phone?: string }) => void;
};
```

Outros métodos (`shutdown`, `boot`, `trackEvent`) ficam para V1.1+.

---

## Comportamento de carregamento

```
[ Página host carrega ]
        │
        ▼
[ <script async src=loader.js> baixa em background ]
        │  (não bloqueia render)
        ▼
[ loader.js carrega widget.<hash>.js ]
        │
        ▼
[ widget.js define <omnidesk-widget> via customElements.define() ]
        │
        ▼
[ widget.js cria <omnidesk-widget> no <body> + Shadow DOM closed ]
        │
        ▼
[ Renderiza apenas o launcher (~5 KB DOM). API NÃO é chamada ainda. ]
        │
   ┌────┴─── usuário clica? ────┐
  não                          sim
   │                            │
   ▼                            ▼
[ Idle ]              [ GET /api/public/widget/init ]
                            │
                            ▼
                      [ Renderiza painel + estado correto baseado em localStorage ]
```

---

## Versionamento

- URL: `https://cdn.omnicare.ia.br/widget/v{MAJOR}/loader.js` — `MAJOR` muda apenas em breaking change de instalação (rara).
- `widget.<hash>.js` é versionado por content-hash (cache busting permanente sem invalidação).
- `loader.js` não é content-hashed — TTL curto (5 min) no Cloudflare. Quando o time de produto faz deploy de novo `widget.<hash>.js`, o `loader.js` é também atualizado para apontar ao novo hash.

---

## CSP do site host

Ao instalar o widget, o tenant precisa permitir as origens:

```
script-src https://cdn.omnicare.ia.br
connect-src https://api.omnicare.ia.br wss://api.omnicare.ia.br
img-src https://minio.omnicare.ia.br data: blob:
```

> Documentação para o tenant: aba "Instalação" do CRM exibe esse trecho com o domínio `api.omnicare.ia.br` substituído por `apiBaseUrl` se customizado.

---

## Compatibilidade de browsers

| Browser | Versão mínima | Justificativa |
|---|---|---|
| Chrome | 90+ | `crypto.randomUUID`, ES2022, custom elements v1 |
| Firefox | 95+ | Idem |
| Safari | 15+ | `crypto.randomUUID` (15.4+); polyfill incluído para 15.0–15.3 |
| Edge | 90+ | Idem Chrome |

> Sem suporte a IE11. Sites com base de usuários em IE11 (raríssimo em V1) recebem tratamento "widget não suportado" silencioso (launcher não renderiza).
