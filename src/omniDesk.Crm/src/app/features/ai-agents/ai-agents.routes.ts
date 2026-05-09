import { Routes } from '@angular/router';

export const aiAgentsRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/agents-list/agents-list.page').then((m) => m.AgentsListPage),
  },
  {
    path: 'avancadas',
    loadComponent: () =>
      import('./pages/ai-settings/ai-settings.page').then((m) => m.AiSettingsPage),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./pages/agent-edit/agent-edit.page').then((m) => m.AgentEditPage),
  },
];
