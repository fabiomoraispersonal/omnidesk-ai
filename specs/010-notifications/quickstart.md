# Quickstart — Notifications (Spec 010)

**Branch**: `010-notifications` | **Phase**: 1 | **Audience**: developers running the feature locally for the first time

This guide walks through the minimum setup to see notifications end-to-end on a dev machine.

---

## 0. Prerequisites

- Specs 002–009 already running locally (Postgres + Redis + Mongo + WhatsApp config seeded).
- `dotnet` 10 + `node` 22 + `npm`.
- A Chromium-based browser (Chrome/Edge/Brave) — Firefox also works for push testing.

---

## 1. Generate VAPID keys (one-time)

The Web Push standard requires a VAPID keypair held by the server. Generate one with the `WebPush` CLI:

```bash
dotnet tool install -g webpush-cli  # if not already
webpush-cli generate-vapid
```

Output:

```
Public Key:  BLc4xRzKlKORKWlbdgFaBrrPK3ydWAH...
Private Key: vMnE3xRzKlKORKWlbdgFaBrrPK3ydW...
```

Save into user-secrets for the API:

```bash
cd src/omniDesk.Api
dotnet user-secrets set "Push:VapidSubject"    "mailto:devops@omnicare.ia.br"
dotnet user-secrets set "Push:VapidPublicKey"  "BLc4xRzKlKORKWlbdgFaBrrPK3ydWAH..."
dotnet user-secrets set "Push:VapidPrivateKey" "vMnE3xRzKlKORKWlbdgFaBrrPK3ydW..."
```

(For prod, set via environment variables `Push__VapidSubject`, `Push__VapidPublicKey`, `Push__VapidPrivateKey`.)

---

## 2. Run the migrations

```bash
cd src/omniDesk.Api

# Public migration (tenant_notification_settings)
dotnet ef database update --context PublicDbContext

# Tenant migration runs automatically per tenant on startup; if a tenant is
# already running, force its schema migration:
dotnet run -- migrate-tenant --slug demo
```

Verify in Postgres:

```bash
psql $DATABASE_URL -c "\d+ tenant_demo.notifications"
psql $DATABASE_URL -c "\d+ tenant_demo.push_subscriptions"
psql $DATABASE_URL -c "\d+ public.tenant_notification_settings"
```

---

## 3. Start the stack

```bash
# Terminal 1 — API
cd src/omniDesk.Api
dotnet run

# Terminal 2 — CRM
cd src/omniDesk.Crm
ng serve --host demo.localhost --port 4200 --ssl
```

The CRM needs HTTPS for Web Push to work (browsers reject SW registration on plain http except `localhost`). Use `ng serve --ssl --ssl-cert localhost.pem --ssl-key localhost-key.pem` (mkcert).

Add `demo.localhost` to `/etc/hosts` if not already.

---

## 4. Verify in-app notifications

1. Log into the CRM as an attendant (existing fixture).
2. Open the bell icon in the header — should show "no unread notifications".
3. In another window (or via API), trigger a ticket assignment:

   ```bash
   curl -X POST https://api.demo.localhost/api/tickets/<ticket_id>/transfer \
     -H "Authorization: Bearer <admin_token>" \
     -H "Content-Type: application/json" \
     -d '{"to_attendant_id":"<other_attendant_id>","reason":"test"}'
   ```

4. Within ~1 second, the badge on the recipient's CRM should increment to **1**.
5. Click the bell → see the notification → click it → land on the ticket → badge clears.

Quick verification SQL:

```sql
SELECT id, event_type, title, is_read, created_at
FROM tenant_demo.notifications
ORDER BY created_at DESC LIMIT 5;
```

---

## 5. Verify browser push

1. In the CRM (after login), the browser should prompt for notification permission. Click **Allow**.
2. Confirm the subscription was registered:

   ```sql
   SELECT id, endpoint, user_agent, created_at
   FROM tenant_demo.push_subscriptions
   WHERE attendant_id = '<your-attendant-id>';
   ```

3. Minimize the CRM tab (or switch to another).
4. Trigger an event from another window (e.g., re-run the transfer in step 4 above).
5. The OS notification should appear within ~5 seconds. Click it → CRM opens/focuses → lands on the ticket.

If push fails to arrive:

- Check `WebPushDispatcher` logs (`docker logs omnidesk-api` or `dotnet run` output).
- If you see `410 Gone`, the subscription was correctly removed; re-register by reloading the CRM.
- If you see `403 Forbidden` from FCM, VAPID config is wrong.

---

## 6. Verify the silence rule

1. Open the CRM as Attendant A. Open ticket TK-X (must be assigned to A).
2. From another window, send a customer message to that conversation (simulating a `ticket.new_message`).
3. **Expected**: in-app feed updates the message bubble in real time; the bell badge does NOT increment for that ticket; no push fires.
4. Repeat for a different ticket TK-Y not currently open → bell increments, push fires.

Inspect the Redis flag:

```bash
redis-cli GET "demo:attendant_active_ticket:<attendant_id>"
# Should return ticket_id while the detail page is open.
```

---

## 7. Test the appointment reminder job

1. Enable the toggle:

   ```bash
   curl -X PUT https://api.demo.localhost/api/notification-settings \
     -H "Authorization: Bearer <tenant_admin_token>" \
     -d '{"follow_up_enabled":false,"reminder_enabled":true,"reminder_time":"20:00"}'
   ```

2. Seed an appointment for tomorrow:

   ```sql
   INSERT INTO tenant_demo.appointments
     (id, contact_id, scheduled_for, status, ticket_id)
   VALUES
     (gen_random_uuid(),
      '<contact-with-phone>',
      (CURRENT_DATE + 1) + TIME '14:00',
      'confirmed',
      NULL);
   ```

3. Trigger the job manually via Hangfire dashboard (`https://api.demo.localhost/hangfire`):

   - Find `appointment-reminder:demo` → "Trigger now"

4. Expected:
   - `wa_message_statuses` has a new row for the appointment's contact.
   - `redis-cli GET "demo:reminder_sent:<appointment_id>:<yyyyMMdd>"` returns `"1"`.
   - Re-trigger the job → no second message sent (idempotency).

5. Failure path: delete the contact's phone, trigger again. Expected:
   - `tenant_demo.tickets.has_reminder_alert = true` if a ticket linked.
   - `tenant_demo.notifications` has a new row with `event_type = "ticket.reminder_failed"` for the responsible attendant.
   - Kanban card shows the ⚠️ badge.

---

## 8. Test the queue monitor

1. Create a ticket that lands in `new` with no attendant (handoff from AI when no attendants online).
2. Wait 5 minutes (or `RecurringJob.Trigger("ticket-queue-monitor")` from the Hangfire dashboard for immediate test — note that the 5-minute condition is on `created_at`, so the ticket must actually be ≥ 5 min old).
3. All supervisors of the ticket's department should receive a `ticket.queued` notification.
4. Idempotency: trigger again → no duplicate notification (`{slug}:queue_alert:{ticket_id}` is set with 1h TTL).

---

## 9. Test manual template send

1. Open a ticket whose conversation's WhatsApp session has expired (or simulate via SQL: `UPDATE wa_conversations SET last_inbound_at = NOW() - INTERVAL '25 hours' WHERE id = ...`).
2. In the ticket detail page, click "Enviar template".
3. Pick an approved template (e.g., `follow_up`), fill variables, see live preview, confirm.
4. Expected: message appears in the conversation thread; `wa_message_statuses` row created; ticket status unchanged.

---

## 10. Run the test suite

```bash
cd src/omniDesk.Api
dotnet test --filter "Category=Notifications|Spec=010"
```

The Testcontainers tests need Docker running. Failed assertions on push delivery require a network — they're tagged `Integration` and can be skipped with `--filter Category!=Integration` for fast CI.

Angular tests:

```bash
cd src/omniDesk.Crm
ng test --include="**/notifications/**" --watch=false
```

---

## 11. Common pitfalls

| Symptom | Likely cause |
|---|---|
| Bell badge never updates | WS not connected; check `/ws/crm` connection in DevTools. |
| Browser push silent | Permission denied; check `Notification.permission` in console. Or VAPID keys mismatched. |
| 410 cycle (subs keep getting removed) | The `endpoint` URL changed; the previous one was invalidated by the browser. Reload CRM to re-subscribe. |
| Reminder job runs but sends nothing | One of the four conditions (toggle, channel, template, phone) is failing — check API logs at job execution time. |
| Reminder runs twice | TZ misconfigured; check `tenants.timezone` for the tenant. |
| SW not registering | CRM not served over HTTPS; or `_headers` missing `Service-Worker-Allowed: /`. |

---

## 12. What you should NOT see

- No email sent to customers for V1 (no SendGrid traffic from Spec 010).
- No `Conversation.New` notifications to supervisors (decision P2 — they only get `ticket.queued` after 5 min).
- No notifications to attendants when AI is handling a conversation (only on handoff to human, which creates a ticket).
