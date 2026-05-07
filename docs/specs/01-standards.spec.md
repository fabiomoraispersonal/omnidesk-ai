# Spec 01 — Padrões Técnicos Globais
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

> Esta spec define padrões transversais que se aplicam a todo o projeto. É leitura obrigatória antes de qualquer implementação de frontend ou configuração de ambiente.

---

## 0. Assets de Marca

> **Fonte de verdade:** `assets/brand/` na raiz do repositório. Ver `assets/brand/README.md` para instruções completas.

| Arquivo | Uso |
|---|---|
| `assets/brand/logo.svg` | Logotipo principal (fundo transparente) |
| `assets/brand/logo-dark.svg` | Variante para dark mode |
| `assets/brand/logo-icon.svg` | Somente o símbolo/ícone (ex: sidebar colapsada) |
| `assets/brand/logo.png` | Fallback PNG (mínimo 512×512) |
| `assets/brand/favicon.ico` | Ícone do browser (32×32) |

### Como usar nos projetos Angular

Copiar os arquivos de `assets/brand/` para o diretório de assets de cada projeto:

```
src/omniDesk.Admin/src/assets/images/
src/omniDesk.Crm/src/assets/images/
```

```html
<!-- Logotipo completo -->
<img src="assets/images/logo.svg" alt="OmniDesk" />

<!-- Somente ícone (ex: sidebar colapsada) -->
<img src="assets/images/logo-icon.svg" alt="OmniDesk" />
```

O `favicon.ico` deve ser copiado para a raiz `src/` de cada projeto Angular e referenciado no `index.html`:

```html
<link rel="icon" type="image/x-icon" href="favicon.ico">
```

### Regras

- **Nunca** hardcode URL de imagem — sempre via `assets/images/`
- Usar sempre o **SVG** como formato principal (escalável, leve)
- O PNG existe apenas como fallback onde SVG não é suportado (ex: templates de e-mail)
- Não alterar os originais em `assets/brand/` — reexportar da ferramenta de design e substituir



| Item | Tecnologia | Versão |
|---|---|---|
| Framework | Angular | 19 (Standalone Components) |
| UI Components | PrimeNG | 17+ |
| Máscaras | ngx-mask | latest |
| Datas | date-fns + date-fns-tz | latest |
| Ícones | PrimeIcons + Lucide Angular | — |
| State (local) | Angular Signals | built-in |
| HTTP | Angular HttpClient | built-in |
| Forms | Reactive Forms | built-in |
| Routing | Angular Router (lazy loading) | built-in |

> **Por que Angular em vez de Next.js:** Next.js tem múltiplas formas de fazer a mesma coisa (App Router, Pages Router, Server Components, Client Components, Server Actions), o que dificulta a padronização em equipes e com IA gerando código. Angular é opinionado por design — uma estrutura clara, uma forma correta de fazer cada coisa.

> **Por que PrimeNG:** Componentes ricos para aplicações de negócio (DataTable, Calendar, Charts, Kanban, Sidebar, Toast), suporte nativo a temas e dark mode, comunidade ativa com .NET/Java ecosystem.

---

## 2. Design System

### 2.0 Princípio de Design

> "Uma ferramenta sofisticada de cuidado com o cliente, e não um sistema técnico."

A tecnologia (IA, CRM, automação) deve ser **invisível na estética**. A interface transmite:
- Sofisticação e leveza
- Clareza sem frieza
- Tecnologia discreta

**Evitar:** aparência de sistema corporativo pesado, dashboards financeiros frios, estética tech agressiva.

### 2.1 Abordagem

- **CSS Custom Properties** (variáveis CSS) como tokens de design — definidas globalmente em `styles/tokens.css`
- **PrimeNG Aura Theme** customizado como base
- Dark mode via classe `.dark` no elemento `<html>`
- Preferência persistida em `localStorage` (`theme: 'dark' | 'light'`)
- Aplicada antes do render via script inline no `index.html` (evita flash de cor errada)

### 2.2 Tokens de Cor

```css
/* ============================= */
/* 🌿 OmniDesk — Identidade Visual */
/* ============================= */

/* styles/tokens.css */

/* === PRIMARY (Verde Oliva) === */
--color-primary-500: #6F7D5C;   /* Principal */
--color-primary-600: #5E6B4E;   /* Hover */
--color-primary-700: #4A563E;   /* Active */

/* === SECONDARY === */
--color-secondary-500: #A8A29E;

/* === SEMANTIC === */
--color-success: #7A9E7E;
--color-warning: #C08A4D;
--color-danger:  #B85C5C;

/* === SURFACE (Light) === */
--color-surface-0:   #FFFFFF;
--color-surface-50:  #F4F1EC;   /* Bg geral (creme quente) */
--color-surface-100: #EDE7DF;   /* Bg hover / item ativo */

/* === SURFACE (Dark) — tons escuros quentes, sem preto puro === */
.dark {
  --color-surface-900: #1E1E1E;   /* Bg principal */
  --color-surface-800: #2A2A2A;   /* Cards / surfaces secundárias */
  --color-surface-0:   #2A2A2A;
  --color-surface-50:  #1E1E1E;
  --color-surface-100: #333333;
}

/* === TEXT === */
--color-text-primary: #2F2F2F;
--color-text-muted:   #7A7A7A;
--color-text-inverse: #FFFFFF;

.dark {
  --color-text-primary: #EFEFEF;
  --color-text-muted:   #9A9A9A;
}

/* === BORDER / ACCENT === */
--color-border: #E5E0D8;
--color-accent: #D6CFC7;

.dark {
  --color-border: #3A3A3A;
  --color-accent: #444444;
}
```

### 2.3 Tipografia

```css
/* Google Fonts: Manrope (weights: 400, 500, 600, 700) */
/* Importar no index.html */

--font-family-base: 'Manrope', 'Inter', system-ui, -apple-system, sans-serif;
--font-family-mono: 'JetBrains Mono', 'Fira Code', monospace;

--font-size-xs:   11px;
--font-size-sm:   13px;
--font-size-base: 14px;
--font-size-md:   16px;
--font-size-lg:   18px;
--font-size-xl:   20px;
--font-size-2xl:  24px;
--font-size-3xl:  30px;

--font-weight-normal:   400;   /* body */
--font-weight-medium:   500;   /* labels */
--font-weight-semibold: 600;   /* headings */
--font-weight-bold:     700;

--line-height-tight:   1.25;
--line-height-normal:  1.5;
--line-height-relaxed: 1.75;
```

### 2.4 Espaçamento e Bordas

```css
--spacing-1:  4px;
--spacing-2:  8px;
--spacing-3:  12px;
--spacing-4:  16px;
--spacing-5:  20px;
--spacing-6:  24px;
--spacing-8:  32px;
--spacing-10: 40px;
--spacing-12: 48px;

/* Bordas arredondadas — entre 8px e 12px, conforme o componente */
--radius-sm:   6px;
--radius-md:   8px;
--radius-lg:   12px;
--radius-xl:   16px;
--radius-full: 9999px;

/* Sombras muito suaves — evitar sombras fortes ou agressivas */
--shadow-xs: 0 1px 2px 0 rgb(0 0 0 / 0.04);
--shadow-sm: 0 1px 4px 0 rgb(0 0 0 / 0.06);
--shadow-md: 0 4px 12px 0 rgb(0 0 0 / 0.07);
--shadow-lg: 0 8px 24px 0 rgb(0 0 0 / 0.08);
```

### 2.5 Guia de Componentes

#### Botões

```css
/* Primário */
.btn-primary {
  background: var(--color-primary-500);
  color: #FFFFFF;
  border: none;
  border-radius: var(--radius-md);
}
.btn-primary:hover { background: var(--color-primary-600); }

/* Secundário */
.btn-secondary {
  background: transparent;
  color: var(--color-text-primary);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}
```

#### Inputs

```css
.p-inputtext, input, textarea, select {
  background: var(--color-surface-50);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}
.p-inputtext:focus {
  border-color: var(--color-primary-500);
  box-shadow: 0 0 0 3px rgb(111 125 92 / 0.15);
}
```

#### Cards

```css
.card {
  background: var(--color-surface-0);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-sm);
  padding: var(--spacing-6);
}
```

#### Sidebar

```css
/* Fundo branco ou creme quente */
.sidebar {
  background: var(--color-surface-0);
  border-right: 1px solid var(--color-border);
}

/* Item de navegação ativo */
.sidebar-item.active {
  background: var(--color-surface-100);
  border-left: 3px solid var(--color-primary-500);
  color: var(--color-primary-600);
  font-weight: var(--font-weight-medium);
}
```

### 2.6 Regras de Design

- **Nunca** usar cores hardcoded no CSS — apenas variáveis dos tokens
- **Nunca** criar componentes visuais que já existem no PrimeNG
- **Sempre** testar dark mode ao criar qualquer nova tela ou componente
- **Dark mode:** nunca usar preto puro (`#000`) — usar os tons escuros quentes definidos nos tokens
- Espaçamento generoso — não comprimir elementos na tela
- Sombras sempre muito suaves — usar no máximo `--shadow-md`
- Ícones: PrimeIcons para ações de UI; Lucide para decorativos/contextuais
- Loading states obrigatórios em toda ação assíncrona
- Empty states com ilustração e CTA quando a lista está vazia

---

## 3. Máscaras de Campos

Usar `ngx-mask`. Configuração global em `app.config.ts`:

```typescript
import { provideNgxMask } from 'ngx-mask';
// ...
providers: [provideNgxMask()]
```

### 3.1 Máscaras Padrão

| Campo | Máscara ngx-mask | Exemplo |
|---|---|---|
| CNPJ | `00.000.000/0000-00` | 12.345.678/0001-99 |
| CPF | `000.000.000-00` | 123.456.789-09 |
| Celular | `(00) 00000-0000` | (11) 98765-4321 |
| Fixo | `(00) 0000-0000` | (11) 3456-7890 |
| Telefone (flex) | `(00) 0000-00009` | Aceita 8 ou 9 dígitos |
| CEP | `00000-000` | 01310-100 |
| Data | `00/00/0000` | 03/05/2026 |
| Hora | `00:00` | 14:30 |
| Cartão de crédito | `0000 0000 0000 0000` | — |

### 3.2 Uso no Template

```html
<!-- CNPJ -->
<input pInputText formControlName="cnpj" mask="00.000.000/0000-00" [showMaskTyped]="true" />

<!-- Telefone flexível (aceita fixo e celular) -->
<input pInputText formControlName="phone" mask="(00) 0000-00009" />

<!-- Moeda (sem ngx-mask — usar p-inputNumber do PrimeNG) -->
<p-inputNumber formControlName="price" mode="currency" currency="BRL" locale="pt-BR" />
```

### 3.3 Armazenamento (Backend)

- Salvar **sem máscara** — apenas dígitos no banco
- CNPJ: `12345678000199` (14 dígitos)
- CPF: `12345678909` (11 dígitos)
- Telefone: `11987654321` (10 ou 11 dígitos)
- CEP: `01310100` (8 dígitos)
- A máscara é apenas visual — o frontend limpa antes de enviar para a API

---

## 4. Validadores Customizados

Localização: `src/app/shared/validators/`

### 4.1 CNPJ (`cnpj.validator.ts`)

```typescript
export function cnpjValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value?.replace(/\D/g, '');
    if (!value || value.length !== 14) return { cnpj: true };
    if (/^(\d)\1{13}$/.test(value)) return { cnpj: true };

    let sum = 0;
    let weight = 5;
    for (let i = 0; i < 12; i++) {
      sum += parseInt(value[i]) * weight;
      weight = weight === 2 ? 9 : weight - 1;
    }
    let digit = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    if (digit !== parseInt(value[12])) return { cnpj: true };

    sum = 0;
    weight = 6;
    for (let i = 0; i < 13; i++) {
      sum += parseInt(value[i]) * weight;
      weight = weight === 2 ? 9 : weight - 1;
    }
    digit = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    if (digit !== parseInt(value[13])) return { cnpj: true };

    return null;
  };
}
```

### 4.2 CPF (`cpf.validator.ts`)

```typescript
export function cpfValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value?.replace(/\D/g, '');
    if (!value || value.length !== 11) return { cpf: true };
    if (/^(\d)\1{10}$/.test(value)) return { cpf: true };

    let sum = 0;
    for (let i = 0; i < 9; i++) sum += parseInt(value[i]) * (10 - i);
    let digit = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    if (digit !== parseInt(value[9])) return { cpf: true };

    sum = 0;
    for (let i = 0; i < 10; i++) sum += parseInt(value[i]) * (11 - i);
    digit = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    if (digit !== parseInt(value[10])) return { cpf: true };

    return null;
  };
}
```

### 4.3 Mensagens de Erro Padrão

```typescript
// shared/utils/form-errors.ts
export const FORM_ERRORS: Record<string, string> = {
  required:  'Campo obrigatório.',
  email:     'E-mail inválido.',
  cnpj:      'CNPJ inválido.',
  cpf:       'CPF inválido.',
  minlength: 'Muito curto.',
  maxlength: 'Muito longo.',
  min:       'Valor abaixo do mínimo.',
  max:       'Valor acima do máximo.',
};
```

---

## 5. Internacionalização e Locale

### 5.1 Configuração Global Angular

```typescript
// app.config.ts
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
registerLocaleData(localePt);

providers: [{ provide: LOCALE_ID, useValue: 'pt-BR' }]
```

### 5.2 Pipes de Formatação — Uso Obrigatório

| Tipo | Pipe | Parâmetros | Saída |
|---|---|---|---|
| Data simples | `date` | `'dd/MM/yyyy'` | 03/05/2026 |
| Data e hora | `date` | `'dd/MM/yyyy HH:mm'` | 03/05/2026 14:30 |
| Hora | `date` | `'HH:mm'` | 14:30 |
| Moeda | `currency` | `'BRL':'symbol':'1.2-2'` | R$ 1.250,00 |
| Número decimal | `number` | `'1.2-2'` | 1.250,50 |
| Percentual | `percent` | `'1.1-1'` | 98,5% |

### 5.3 Datas e Timezone

- API retorna datas em **UTC** (ISO 8601 com `Z`): `"2026-06-03T17:30:00Z"`
- Frontend converte para o timezone do tenant antes de exibir:

```typescript
import { toZonedTime, format } from 'date-fns-tz';
import { ptBR } from 'date-fns/locale';

function toDisplay(utcDate: string, timezone: string): string {
  const zoned = toZonedTime(new Date(utcDate), timezone);
  return format(zoned, 'dd/MM/yyyy HH:mm', { locale: ptBR });
}
```

- Ao enviar datas para a API, converter de volta para UTC:

```typescript
import { fromZonedTime } from 'date-fns-tz';
const utc = fromZonedTime(localDate, tenantTimezone).toISOString();
```

### 5.4 Idioma — Regras de Ouro

| Contexto | Idioma | Exemplos |
|---|---|---|
| Código TypeScript / C# | Inglês | `getUserById()`, `firstName`, `createdAt` |
| Templates HTML | Português-BR | `"Nome completo"`, `"Salvar alterações"` |
| Mensagens de erro (usuário) | Português-BR | `"CNPJ inválido."` |
| Comentários de código | Inglês | `// Returns user by tenant-scoped ID` |
| Logs do sistema | Inglês | `"Ticket assigned to attendant"` |
| Nomes de rotas da API | Inglês | `/api/appointments`, `/api/auth/login` |

---

## 6. Encoding

### 6.1 `.editorconfig` (raiz do projeto — obrigatório)

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
indent_style = space
indent_size = 2
trim_trailing_whitespace = true
insert_final_newline = true

[*.md]
trim_trailing_whitespace = false

[*.{cs,csproj}]
indent_size = 4
```

### 6.2 Backend (C# .NET)

```csharp
// Program.cs — garantir UTF-8 na saída
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Respostas HTTP
app.Use(async (context, next) => {
    context.Response.ContentType = "application/json; charset=utf-8";
    await next();
});
```

### 6.3 Banco de Dados (PostgreSQL)

```sql
-- Configuração do banco
CREATE DATABASE omnidesk
  ENCODING 'UTF8'
  LC_COLLATE 'pt_BR.UTF-8'
  LC_CTYPE 'pt_BR.UTF-8';

-- Verificar
SHOW client_encoding; -- deve retornar UTF8
```

### 6.4 Regras

- Nunca salvar arquivos com BOM (Byte Order Mark) — configurar IDE para desativar
- Angular CLI e .NET CLI já geram UTF-8 por padrão — não alterar
- Variáveis de ambiente: apenas ASCII nos nomes, UTF-8 nos valores se necessário
- Imports de fontes externas (JSON, CSVs): sempre validar encoding antes de processar

---

## 7. Configuração de Locale no Tenant

Os seguintes campos de locale fazem parte da entidade `public.tenants` (Spec 03):

| Campo | Tipo | V1 | Descrição |
|---|---|---|---|
| `timezone` | varchar(50) | configurável | Default: `America/Sao_Paulo`. Formato IANA. |
| `locale` | varchar(10) | fixo `pt-BR` | Locale BCP 47. V2: configurável. |
| `currency` | varchar(3) | fixo `BRL` | ISO 4217. V2: configurável. |
| `date_format` | varchar(20) | fixo `dd/MM/yyyy` | V2: configurável. |

> **V1:** Apenas `timezone` é editável pelo `saas_admin` ao criar o tenant — dropdown com fusos brasileiros: `America/Sao_Paulo`, `America/Manaus`, `America/Belem`, `America/Fortaleza`, `America/Recife`, `America/Noronha`, `America/Porto_Velho`, `America/Boa_Vista`, `America/Rio_Branco`. Os demais são constantes no código.

---

## 8. Critérios de Aceite

- [ ] `.editorconfig` na raiz do projeto com charset UTF-8
- [ ] Todos os arquivos salvos sem BOM
- [ ] `LOCALE_ID = 'pt-BR'` configurado no `app.config.ts`
- [ ] Datas sempre exibidas no formato `dd/MM/yyyy` via pipe `date`
- [ ] Moedas sempre exibidas como `R$ X.XXX,XX` via pipe `currency` ou `p-inputNumber`
- [ ] Dark mode funcional em todas as telas — alternado por toggle no header
- [ ] Preferência de tema persistida em `localStorage`
- [ ] Nenhuma cor hardcoded no CSS — apenas tokens (`--color-*`)
- [ ] Campos de CNPJ com máscara e validador de dígitos verificadores
- [ ] Campos de CPF com máscara e validador de dígitos verificadores
- [ ] Campos de telefone com máscara flexível (fixo e celular)
- [ ] Campos de CEP com máscara
- [ ] Backend envia apenas dígitos (sem máscara) para campos CNPJ/CPF/telefone/CEP
- [ ] API retorna datas em UTC; frontend converte para timezone do tenant antes de exibir
- [ ] Código TypeScript/C# escrito em inglês; templates HTML em português-BR
- [ ] Sem strings de UI hardcoded em TypeScript
- [ ] `tenant.timezone` configurável pelo `saas_admin` com dropdown de fusos brasileiros
