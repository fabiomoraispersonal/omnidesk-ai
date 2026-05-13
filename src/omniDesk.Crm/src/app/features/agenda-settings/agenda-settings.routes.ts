import { Routes } from '@angular/router';

/** Spec 011 US6 (T138) — agenda settings route. */
export const agendaSettingsRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./settings-page.component').then(m => m.AgendaSettingsPageComponent),
  },
];
