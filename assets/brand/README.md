# OmniDesk — Brand Assets

Esta pasta é a **fonte de verdade** para todos os ativos de marca do OmniDesk.
Não servir diretamente pela aplicação — copiar para os projetos Angular conforme necessário.

---

## Estrutura esperada

```
assets/brand/
├── README.md               ← este arquivo
├── logo.svg                ← logotipo principal (fundo transparente)
├── logo-dark.svg           ← variante para dark mode (se houver versão clara)
├── logo-icon.svg           ← somente o símbolo/ícone, sem o texto
├── logo.png                ← fallback PNG (mínimo 512x512)
└── favicon.ico             ← ícone do browser (32x32)
```

## Como usar nos projetos Angular

Copie os arquivos necessários para:

```
src/omniDesk.Admin/src/assets/images/
src/omniDesk.Crm/src/assets/images/
```

No template Angular:

```html
<!-- Logotipo principal -->
<img src="assets/images/logo.svg" alt="OmniDesk" />

<!-- Somente ícone (ex: sidebar colapsada) -->
<img src="assets/images/logo-icon.svg" alt="OmniDesk" />
```

## Regras

- **Nunca** alterar os arquivos originais desta pasta — editar na ferramenta de design e reexportar
- **Sempre** manter a versão SVG como principal — é escalável e tem menor tamanho
- O PNG existe apenas como fallback onde SVG não é suportado (ex: e-mails)
- O `favicon.ico` deve ser copiado para `src/omniDesk.Admin/src/` e `src/omniDesk.Crm/src/`

## Status atual

| Arquivo | Status |
|---|---|
| `logo.svg` | ⏳ Aguardando arquivo do cliente |
| `logo-dark.svg` | ⏳ Opcional — criar se necessário |
| `logo-icon.svg` | ⏳ Aguardando arquivo do cliente |
| `logo.png` | ⏳ Aguardando arquivo do cliente |
| `favicon.ico` | ⏳ Aguardando arquivo do cliente |

> Substitua os `⏳` por `✅` após adicionar cada arquivo.
