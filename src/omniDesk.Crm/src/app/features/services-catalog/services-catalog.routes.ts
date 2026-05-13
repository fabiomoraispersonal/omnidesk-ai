import { Routes } from '@angular/router';
import { roleGuard } from '../../core/authorization/role.guard';

export const servicesCatalogRoutes: Routes = [
  {
    path: '',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./services-list.component').then(m => m.ServicesListComponent),
  },
  {
    path: 'novo',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./service-form.component').then(m => m.ServiceFormComponent),
  },
  {
    path: ':id',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./service-form.component').then(m => m.ServiceFormComponent),
  },
];
