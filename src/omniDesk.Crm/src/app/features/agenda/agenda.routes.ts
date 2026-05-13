import { Routes } from '@angular/router';

/** Spec 011 US3 (T109) — rotas do módulo de agenda. */
export const agendaRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./agenda-page.component').then(m => m.AgendaPageComponent),
    children: [
      { path: '', redirectTo: 'grade', pathMatch: 'full' },
      {
        path: 'grade',
        loadComponent: () =>
          import('./weekly-grid.component').then(m => m.WeeklyGridComponent),
      },
      {
        path: 'agendamentos',
        loadComponent: () =>
          import('./appointments-list.component').then(m => m.AppointmentsListComponent),
      },
      {
        path: 'pendentes',
        loadComponent: () =>
          import('./pending-appointments.component').then(m => m.PendingAppointmentsComponent),
      },
    ],
  },
  {
    path: 'agendamentos/novo',
    loadComponent: () =>
      import('./appointment-form.component').then(m => m.AppointmentFormComponent),
  },
  {
    path: 'agendamentos/:id',
    loadComponent: () =>
      import('./appointment-detail.component').then(m => m.AppointmentDetailComponent),
  },
];
