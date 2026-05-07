import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: 'tenants',
    loadChildren: () =>
      import('./features/tenants/tenants.routes').then(m => m.TENANTS_ROUTES),
  },
  {
    path: 'agent-templates',
    loadChildren: () =>
      import('./features/agent-templates/agent-templates.routes').then(m => m.AGENT_TEMPLATES_ROUTES),
  },
  { path: '', redirectTo: 'tenants', pathMatch: 'full' },
];
