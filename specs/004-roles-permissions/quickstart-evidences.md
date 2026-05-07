# Quickstart Evidences — Spec 004

> Generated as a placeholder by the implementation pipeline.
> Operator must execute the manual flows in `quickstart.md` §§ B–E and replace the
> sections below with timestamps, screenshots, and Mongo log excerpts.

## B) Impersonation flow

- [ ] Pre-conditions verified (tenant `clinica-x` active)
- [ ] `POST /admin/tenants/clinica-x/impersonation` returned `expiresAt = now + 5 min`
- [ ] CRM displayed banner with `MM:SS` countdown
- [ ] Mongo log entry contained `Impersonating: true`, `ImpersonatedBy: saas_admin`
- [ ] Token expired naturally; refresh attempt rejected with `400 impersonation_not_refreshable`

## C) Deactivation invalidation

- [ ] Victim received 401 within 1 s after deactivation
- [ ] CI metric `Deactivate_RevokesNextRequestWithinOneSecond` passing

## D) Last tenant_admin block

- [ ] Sole `tenant_admin` deactivation returned 422 with PT-BR message

## E) Attendant scope

- [ ] Attendant assigned to Dept A saw only Dept A tickets
- [ ] Attendant with 0 departments saw 0 tickets
- [ ] Attendant with 2 departments saw both
