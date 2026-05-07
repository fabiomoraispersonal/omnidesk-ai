import { Injectable, computed, inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

export const ROLES = {
  SaasAdmin: 'saas_admin',
  TenantAdmin: 'tenant_admin',
  Supervisor: 'supervisor',
  Attendant: 'attendant',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

const RANK: Record<Role, number> = {
  [ROLES.SaasAdmin]: 0,
  [ROLES.TenantAdmin]: 3,
  [ROLES.Supervisor]: 2,
  [ROLES.Attendant]: 1,
};

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

export function isAtLeast(actual: Role | null, minimum: Role): boolean {
  if (!actual) return false;
  if (actual === ROLES.SaasAdmin || minimum === ROLES.SaasAdmin) return false;
  return RANK[actual] >= RANK[minimum];
}

@Injectable({ providedIn: 'root' })
export class RoleSignal {
  private readonly authService = inject(AuthService);

  readonly role = computed<Role | null>(() => normalize(this.authService.currentUser()?.role));
  readonly isImpersonating = computed<boolean>(() => !!this.authService.currentUser()?.isImpersonation);
  readonly isAtLeastSupervisor = computed(() => isAtLeast(this.role(), ROLES.Supervisor));
  readonly isAtLeastTenantAdmin = computed(() => isAtLeast(this.role(), ROLES.TenantAdmin));
}
