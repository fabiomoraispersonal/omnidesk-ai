# Spec 007 — Quickstart evidences

Preencher após validação manual com Docker compose ativo (Postgres + Redis + Mongo + MinIO).

| QS | Cenário | Status | Evidência (screenshot/log) |
|---|---|---|---|
| QS-1 | Visitante envia 1ª mensagem via widget e recebe resposta da IA | ⬜ | |
| QS-2 | Admin altera config no CRM e widget reflete | ⬜ | |
| QS-3 | Atendente recebe transbordo e responde via CRM | ⬜ | |
| QS-4 | Visitante retorna e retoma conversa | ⬜ | |
| QS-5 | Conversas idle abandoned/inactivity automaticamente | ⬜ | |
| QS-6 | Visitante envia anexo PNG/PDF ≤10MB; rejeita >10MB e MIME inválido | ⬜ | |
| QS-7 | WS reconecta após pausa do servidor mock; nada duplica | ⬜ | |
| QS-8 | Smoke E2E Playwright (P1 happy path) | ⬜ | |
| QS-9 | Admin desativa widget e conversas abertas fecham | ⬜ | |

## Como rodar

1. `docker compose up -d` na raiz do repo (Postgres 16 + Redis 7 + Mongo 7 + MinIO).
2. `dotnet run --project src/omniDesk.Api/omniDesk.Api.csproj`.
3. `pnpm --filter omniDesk.Widget run dev` para o widget bundle.
4. Abrir `http://localhost:5173/widget/v1/dev-test.html` para QS-1, QS-4, QS-6, QS-7.
5. Para QS-2/QS-3/QS-9, abrir o CRM Angular (`ng serve` no `omniDesk.Crm`) e fazer
   login como `tenant_admin` (QS-2/QS-9) ou `attendant` (QS-3).
