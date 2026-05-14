import { Routes } from '@angular/router';

export const auditRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./audit-activity/audit-activity.component').then(m => m.AuditActivityComponent),
  },
];
