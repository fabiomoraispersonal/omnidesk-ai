import { Routes } from '@angular/router';

export const AGENT_TEMPLATES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./agent-template-list/agent-template-list.component').then(m => m.AgentTemplateListComponent),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./agent-template-form/agent-template-form.component').then(m => m.AgentTemplateFormComponent),
  },
  {
    path: ':id/edit',
    loadComponent: () =>
      import('./agent-template-form/agent-template-form.component').then(m => m.AgentTemplateFormComponent),
  },
];
