import { Routes } from '@angular/router';
import { roleGuard } from './core/authorization/role.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./features/auth/reset-password/reset-password.component').then(m => m.ResetPasswordComponent),
  },
  {
    path: 'accept-invite',
    loadComponent: () =>
      import('./features/auth/accept-invite/accept-invite.component').then(m => m.AcceptInviteComponent),
  },
  {
    path: 'impersonate',
    loadComponent: () =>
      import('./features/auth/impersonation/impersonation-handler.component').then(m => m.ImpersonationHandlerComponent),
  },
  {
    path: 'acesso-suspenso',
    loadComponent: () =>
      import('./features/auth/access-suspended/access-suspended.component').then(m => m.AccessSuspendedComponent),
  },
  {
    path: 'configuracoes/agentes-de-ia',
    loadChildren: () =>
      import('./features/ai-agents/ai-agents.routes').then(m => m.aiAgentsRoutes),
  },
  {
    path: 'configuracoes/live-chat',
    loadComponent: () =>
      import('./features/live-chat-config/live-chat-config.component').then(
        (m) => m.LiveChatConfigComponent,
      ),
  },
  {
    path: 'configuracoes/whatsapp',
    loadComponent: () =>
      import('./features/whatsapp-config/whatsapp-config.component').then(
        (m) => m.WhatsAppConfigComponent,
      ),
  },
  {
    path: 'configuracoes/whatsapp/templates',
    loadComponent: () =>
      import('./features/whatsapp-templates/whatsapp-templates.component').then(
        (m) => m.WhatsAppTemplatesComponent,
      ),
  },
  {
    path: 'live-chat',
    loadComponent: () =>
      import('./features/live-chat-inbox/live-chat-inbox.component').then(
        (m) => m.LiveChatInboxComponent,
      ),
  },
  // Spec 009 US2 — Tickets / Kanban routes
  {
    path: 'kanban',
    loadComponent: () =>
      import('./features/tickets-kanban/tickets-kanban.component').then(
        (m) => m.TicketsKanbanComponent,
      ),
  },
  {
    path: 'tickets/:id',
    loadComponent: () =>
      import('./features/ticket-detail/ticket-detail.component').then(
        (m) => m.TicketDetailComponent,
      ),
  },
  // Spec 009 US9 — Pipeline config (tenant_admin only)
  {
    path: 'settings/pipelines/:departmentId',
    canActivate: [roleGuard('tenant_admin')],
    loadComponent: () =>
      import('./features/pipeline-config/pipeline-config.component').then(
        (m) => m.PipelineConfigComponent,
      ),
  },
  // Spec 009 US6 — Contact profile
  {
    path: 'contacts/:id',
    loadComponent: () =>
      import('./features/contacts/contact-profile.component').then(
        (m) => m.ContactProfileComponent,
      ),
  },
  // Alias: keep /conversations working (Spec 007)
  {
    path: 'conversations',
    loadComponent: () =>
      import('./features/live-chat-inbox/live-chat-inbox.component').then(
        (m) => m.LiveChatInboxComponent,
      ),
  },
  { path: '', redirectTo: 'kanban', pathMatch: 'full' },
];
