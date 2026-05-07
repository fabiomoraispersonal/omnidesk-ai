# Discovery — Programa de Pontos / Cartão de Benefícios
**Status:** 🔍 Em discovery — não implementar na V1
**Origem:** Conversa com o produto em 2026-06
**Relacionado a:** Spec 03 (Agenda e Catálogo de Serviços), entidade `services`

---

## Ideia Central

Criar um sistema de **fidelização por pontos** integrado ao catálogo de serviços da clínica. A medida que o cliente usa serviços (consultas, procedimentos, etc.), acumula pontos que podem ser resgatados por outros serviços ou benefícios dentro da própria plataforma.

Contexto: funciona como um **cartão de benefícios / cashback** interno, sem integração com sistemas externos de pagamento na V1 do conceito.

---

## Impacto no Catálogo de Serviços (Spec 02)

Quando o programa for implementado, será necessário adicionar ao cadastro de serviços (`services`):

| Campo | Tipo | Descrição |
|---|---|---|
| `points_value` | int | Quantidade de pontos que este serviço gera. `0` = não elegível ao programa de pontos. |

> **Decisão V1:** Este campo pode ser adicionado já na V1 com `DEFAULT 0` (sem funcionalidade ativa) para facilitar a migração futura, sem precisar de migration de schema. A discutir com o time técnico.

---

## Perguntas de Discovery (a responder com o cliente)

### Geração de Pontos

- [ ] **P1. Quando os pontos são gerados?**
  - a) Automaticamente ao marcar o agendamento como **concluído**
  - b) Manualmente pelo atendente após confirmar pagamento
  - c) Automático com possibilidade de correção manual

- [ ] **P2. Cancelamento retroativo de pontos**
  - Se um agendamento que já gerou pontos for cancelado depois, os pontos são estornados automaticamente ou o atendente decide?

### Valor e Conversão

- [ ] **P3. Modelo de conversão de pontos**
  - a) Por valor monetário: cada R$1 gasto = X pontos (taxa configurável)
  - b) Por serviço: cada serviço tem um valor fixo em pontos no catálogo
  - c) Misto: base pelo preço + bônus por serviço específico
  - **Impacto:** define se o campo `points_value` no catálogo é suficiente ou se é necessária uma `conversion_rate` global

- [ ] **P4. Quem define a taxa de conversão?** Admin do SaaS globalmente ou cada tenant configura livremente?

### Resgate (Saídas)

- [ ] **P5. O que o atendente faz ao debitar pontos manualmente?**
  - a) Aplica desconto em valor (ex: 500 pontos = R$50 de desconto)
  - b) Troca por um serviço inteiro (ex: 1000 pontos = 1 consulta gratuita)
  - c) Ambos os cenários existem (precisaria de tipo na transação)

- [ ] **P6. V2: resgate automático no agendamento?**
  - Cliente escolhe usar pontos na hora de agendar e o sistema debita automaticamente?

### Experiência do Cliente

- [ ] **P7. Visibilidade do saldo para o cliente**
  - a) Só o atendente/CRM vê — cliente pergunta ao atendente
  - b) Cliente pergunta via WhatsApp → IA responde com o saldo
  - c) Portal/área do cliente (V3?)

- [ ] **P8. Opt-in ou opt-out?**
  - Todo cliente participa automaticamente, ou precisa aceitar participar do programa?

- [ ] **P9. Nome da moeda**
  - Genérico ("Pontos") ou o tenant pode customizar? (ex: "Pontos Saúde", "Créditos Clínica X")

### Configuração e Escopo

- [ ] **P11. Expiração dos pontos — como contar?**
  - a) A partir da data em que foram ganhos (ex: ganhou em Jan, expira em Jul)
  - b) A partir da última movimentação (reinicia o contador a cada uso)
  - c) Data fixa no ano (ex: todo ano em 31/Dez)
  - Notificar o cliente antes de expirar? Via WhatsApp?

- [ ] **P12. Escopo do programa**
  - Cada tenant tem seu próprio programa independente (padrão multi-tenant)?
  - Ou é algo centralizado no SaaS?

- [ ] **P12. Regras anti-fraude**
  - Limite de pontos por período? Aprovação de resgates acima de um valor?

---

## Módulos do Sistema que Seriam Afetados

| Módulo | Spec | Impacto esperado |
|---|---|---|
| Catálogo de Serviços | Spec 02 | Campo `points_value` no cadastro de serviço |
| Agenda | Spec 02 | Trigger de geração de pontos ao concluir agendamento |
| Contatos/CRM | Spec 02 | Saldo de pontos visível no perfil do cliente |
| WhatsApp / IA | Spec 02, 02 | Tool call para consultar saldo (V2) |
| Notificações | Spec 02 | Notificação de pontos ganhos / prestes a expirar |
| Auditoria | Spec 02 | Log de todas as movimentações de pontos |

---

## Entidades Prováveis (esboço inicial — não definitivo)

```
loyalty_programs          ← Configuração do programa por tenant
  - tenant_id
  - is_active
  - points_currency_name  ← ex: "Pontos", "Créditos Saúde"
  - expiration_days       ← dias até expirar (0 = não expira)
  - expiration_mode       ← 'from_earn' | 'from_last_activity' | 'fixed_date'

client_points_balance     ← Saldo atual por cliente
  - contact_id
  - total_points
  - updated_at

client_points_ledger      ← Extrato (entrada e saída)
  - contact_id
  - type                  ← 'earn' | 'redeem' | 'expire' | 'adjustment'
  - points
  - description
  - reference_id          ← FK para appointment, ticket, etc.
  - created_by            ← attendant_id (para saídas manuais)
  - expires_at            ← data de expiração desta entrada
  - created_at
```

---

## Próximos Passos para o Discovery

1. **Sessão com o cliente** — responder as perguntas do bloco acima
2. **Benchmark** — analisar como clínicas/sistemas similares implementam fidelização (Doctoralia, TuoTempo, etc.)
3. **Definir modelo de conversão** — taxa fixa vs. por serviço vs. misto
4. **Validar entidades** — confirmar se o ledger duplo (balance + extrato) é o modelo correto
5. **Escrever a Spec 02** — apenas após o discovery estar completo

---

> **Nota:** Não iniciar implementação de nenhum item desta discovery sem aprovação formal e criação da Spec correspondente.
