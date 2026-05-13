import { Routes } from '@angular/router';
import { roleGuard } from '../../core/authorization/role.guard';

/** Spec 011 US2 (T069) — rotas do módulo de profissionais. */
export const professionalsRoutes: Routes = [
  {
    path: '',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./professionals-list.component').then(m => m.ProfessionalsListComponent),
  },
  {
    path: 'novo',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./professional-form.component').then(m => m.ProfessionalFormComponent),
  },
  {
    path: ':id',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./professional-detail.component').then(m => m.ProfessionalDetailComponent),
  },
  {
    path: ':id/editar',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./professional-form.component').then(m => m.ProfessionalFormComponent),
  },
];
