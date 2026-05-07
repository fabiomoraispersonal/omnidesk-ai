import { Injectable, computed, inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

export const ROLES = {
  SaasAdmin: 'saas_admin',
  TenantAdmin: 'tenant_admin',
  Supervisor: 'supervisor',
  Attendant: 'attendant',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

function normalize(raw: string | null | undefined): Role | null {
  if (!raw) return null;
  switch (raw) {
    case 'SaasAdmin': return ROLES.SaasAdmin;
    case 'TenantAdmin': return ROLES.TenantAdmin;
    case 'Supervisor': return ROLES.Supervisor;
    case 'Attendant': return ROLES.Attendant;
  }
  const lower = raw.toLowerCase();
  return (Object.values(ROLES) as string[]).includes(lower) ? (lower as Role) : null;
}

@Injectable({ providedIn: 'root' })
export class RoleSignal {
  private readonly authService = inject(AuthService);

  readonly role = computed<Role | null>(() => normalize(this.authService.currentUser()?.role));

  readonly isSaasAdmin = computed(() => this.role() === ROLES.SaasAdmin);
}
