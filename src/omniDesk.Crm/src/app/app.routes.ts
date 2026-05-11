import { Routes } from '@angular/router';

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
    path: 'live-chat',
    loadComponent: () =>
      import('./features/live-chat-inbox/live-chat-inbox.component').then(
        (m) => m.LiveChatInboxComponent,
      ),
  },
  { path: '', redirectTo: 'login', pathMatch: 'full' },
];
