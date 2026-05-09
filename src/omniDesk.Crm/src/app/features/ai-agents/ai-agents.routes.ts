import { Routes } from '@angular/router';

export const aiAgentsRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/agents-list/agents-list.page').then((m) => m.AgentsListPage),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./pages/agent-edit/agent-edit.page').then((m) => m.AgentEditPage),
  },
];
