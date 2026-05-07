import { Routes } from '@angular/router';
import { saasAdminGuard } from './core/authorization/role.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: 'tenants',
    canActivate: [saasAdminGuard],
    loadChildren: () =>
      import('./features/tenants/tenants.routes').then(m => m.TENANTS_ROUTES),
  },
  {
    path: 'agent-templates',
    canActivate: [saasAdminGuard],
    loadChildren: () =>
      import('./features/agent-templates/agent-templates.routes').then(m => m.AGENT_TEMPLATES_ROUTES),
  },
  { path: '', redirectTo: 'tenants', pathMatch: 'full' },
];
