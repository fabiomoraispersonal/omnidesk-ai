# Quickstart Evidences — Spec 005

> Placeholder. Operador deve executar os fluxos de `quickstart.md` em ambiente
> local e substituir os checkboxes abaixo com timestamps + screenshots/links
> para os logs (Mongo) ou métricas (CI). Não comitar dados sensíveis.

## A) Cadastro de departamento + atendentes (US1)

- [ ] Tenant admin cria 2 departamentos (Comercial, Suporte) — log do evento + tabela
- [ ] Cadastra 3 atendentes vinculados a 1+ deptos
- [ ] `GET /api/departments/{id}/attendants` retorna a lista correta

## B) Distribuição round-robin (US2)

- [ ] 6 tickets distribuídos entre 3 atendentes online → 2 cada (diff=0)
- [ ] Métrica em CI: `RoundRobinCursorTests.DistributesEvenlyAcross100Tickets` ✅

## C) Lock de concorrência (US3)

- [ ] 50 pares concorrentes de `POST /pickup` → 0 atribuições duplicadas
- [ ] Métrica em CI: `ConcurrentPickupTests.FiftyConcurrentPairs_*` ✅

## D) Transferência (US4)

- [ ] Cross-dept transfer recalcula SLA (assert `sla_started_at` atualizado)
- [ ] Histórico íntegro pós-transferência

## E) Presença + timeouts (US5)

- [ ] 15 min sem heartbeat → `away` (Mongo log com `changed_by=system`)
- [ ] 45 min total → `offline`
- [ ] Painel do supervisor recebe evento em ≤ 1 s

## F) Canned responses (US6)

- [ ] Resposta `Olá {{client_name}}, sou {{attendant_name}} do {{department_name}}` renderizada com valores reais
- [ ] Variável desconhecida `{{foo}}` preservada literalmente + log Warning

## G) Transbordo (US7)

- [ ] Matriz 4×4 verificada (in/out hours × any-online/none)
- [ ] Mensagem da IA condizente com `QueueReason` em cada caso

## H) Sugestão IA (US8)

- [ ] Botão "Sugerir resposta" → preview editável (≤ 3 s)
- [ ] **Nada enviado ao cliente** sem clique em "Aprovar/Editar e enviar" (SC-007)
- [ ] Mongo `ai_suggestion_logs` com `human_action=approved|edited|discarded`
- [ ] Provedor offline → toast PT-BR; conversa segue normal (FR-040)

## I) SLA visual (US9)

- [ ] Ticket criado às 17h50 com SLA 60 min em dept 09–18: badge ainda Ok no fim do dia, vira amarelo às 80% úteis na manhã seguinte
- [ ] Ticket sem SLA configurado: nenhum badge

## J) Integração com Specs 002/003/004

- [ ] `dept_ids` na claim do JWT populado por `attendant_departments` (T090)
- [ ] `Policies.Can*Attendant`/`Can*Department` autorizam corretamente
- [ ] Nenhum endpoint da Spec 005 sem policy declarada (T094)
