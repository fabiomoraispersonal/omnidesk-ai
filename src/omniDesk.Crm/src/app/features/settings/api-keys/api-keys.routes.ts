import { Routes } from '@angular/router';

export const apiKeysRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./api-keys.component').then(m => m.ApiKeysComponent),
  },
];
