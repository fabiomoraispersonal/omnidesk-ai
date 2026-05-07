import { Routes } from '@angular/router';

export const TENANTS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./tenant-list/tenant-list.component').then(m => m.TenantListComponent),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./tenant-create/tenant-create.component').then(m => m.TenantCreateComponent),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./tenant-detail/tenant-detail.component').then(m => m.TenantDetailComponent),
  },
  {
    path: ':id/health',
    loadComponent: () =>
      import('./tenant-health-dashboard/tenant-health-dashboard.component').then(
        m => m.TenantHealthDashboardComponent),
  },
];
