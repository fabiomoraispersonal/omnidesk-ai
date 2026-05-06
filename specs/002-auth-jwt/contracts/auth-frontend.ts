/**
 * Contratos do frontend para o módulo de Autenticação
 * Branch: 002-auth-jwt
 *
 * Define as interfaces que AuthService, TokenService e Guards devem implementar.
 * Agnóstico de framework — a implementação vive em src/omniDesk.*/

import { Observable } from 'rxjs';

// ---------------------------------------------------------------------------
// Tipos compartilhados
// ---------------------------------------------------------------------------

export type UserRole = 'saas_admin' | 'tenant_admin' | 'supervisor' | 'attendant';

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  tenantSlug: string | null;
  totpEnabled: boolean;
  isImpersonation: boolean;
}

export interface ActiveSession {
  id: string;
  userAgent: string | null;
  ipAddress: string | null;
  createdAt: string;
  isCurrent: boolean;
}

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  turnstileToken: string;
}

export interface TotpVerifyRequest {
  totpSessionToken: string;
  code: string;
}

export interface ForgotPasswordRequest {
  email: string;
  turnstileToken: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
}

export interface AcceptInviteRequest {
  token: string;
  name: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface UpdateProfileRequest {
  name: string;
}

// ---------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------

export interface LoginSuccess {
  accessToken: string;
  user: Pick<AuthUser, 'id' | 'name' | 'role' | 'tenantSlug'>;
}

export interface LoginTotpRequired {
  requiresTotp: true;
  totpSessionToken: string;
}

export type LoginResponse = LoginSuccess | LoginTotpRequired;

export interface TotpSetupResponse {
  qrCodeUri: string;
  secret: string;
}

export interface TotpConfirmResponse {
  recoveryCodes: string[];
}

// ---------------------------------------------------------------------------
// IAuthService — contrato principal
// ---------------------------------------------------------------------------

export interface IAuthService {
  /** Usuário atenticado atual. null = não autenticado. */
  readonly currentUser: AuthUser | null;

  /** Login com e-mail + senha + token Turnstile. */
  login(request: LoginRequest): Observable<LoginResponse>;

  /** Completa login após verificação TOTP. */
  verifyTotp(request: TotpVerifyRequest): Observable<LoginSuccess>;

  /** Tenta restaurar sessão a partir do cookie de refresh (chamado no bootstrap). */
  restoreSession(): Observable<boolean>;

  /** Encerra a sessão atual. */
  logout(): Observable<void>;

  /** Solicita link de recuperação de senha. */
  forgotPassword(request: ForgotPasswordRequest): Observable<void>;

  /** Redefine senha com token do e-mail. */
  resetPassword(request: ResetPasswordRequest): Observable<void>;

  /** Aceita convite e cria conta. */
  acceptInvite(request: AcceptInviteRequest): Observable<LoginSuccess>;

  /** Retorna true se o usuário atual possui pelo menos uma das roles. */
  hasRole(...roles: UserRole[]): boolean;
}

// ---------------------------------------------------------------------------
// ITokenService — armazenamento em memória do access token
// ---------------------------------------------------------------------------

export interface ITokenService {
  /** Access token atual. null = não autenticado. */
  readonly accessToken: string | null;

  /** Armazena o access token em memória. */
  setToken(token: string): void;

  /** Remove o token da memória (logout local). */
  clearToken(): void;

  /** Retorna true se o token está presente e não expirado. */
  isTokenValid(): boolean;
}

// ---------------------------------------------------------------------------
// ISessionService — gestão de sessões ativas
// ---------------------------------------------------------------------------

export interface ISessionService {
  listSessions(): Observable<ActiveSession[]>;
  revokeSession(sessionId: string): Observable<void>;
  revokeAllOtherSessions(): Observable<void>;
}

// ---------------------------------------------------------------------------
// IProfileService — perfil e senha
// ---------------------------------------------------------------------------

export interface IProfileService {
  getProfile(): Observable<AuthUser>;
  updateProfile(request: UpdateProfileRequest): Observable<AuthUser>;
  changePassword(request: ChangePasswordRequest): Observable<void>;
}

// ---------------------------------------------------------------------------
// ITotpService — configuração de 2FA
// ---------------------------------------------------------------------------

export interface ITotpService {
  setup(): Observable<TotpSetupResponse>;
  confirm(code: string): Observable<TotpConfirmResponse>;
  disable(password: string): Observable<void>;
}

// ---------------------------------------------------------------------------
// Route Guard contracts
// ---------------------------------------------------------------------------

/**
 * AuthGuard: redireciona para /login se não autenticado.
 * Deve chamar AuthService.restoreSession() antes de verificar.
 */
export interface IAuthGuard {
  canActivate(): Observable<boolean>;
}

/**
 * RoleGuard: redireciona para /acesso-negado se role não autorizada.
 * Recebe roles permitidas via ActivatedRouteSnapshot.data['roles'].
 */
export interface IRoleGuard {
  canActivate(allowedRoles: UserRole[]): Observable<boolean>;
}

/**
 * GuestGuard: redireciona para o painel principal se já autenticado.
 * Usado nas rotas de login e accept-invite.
 */
export interface IGuestGuard {
  canActivate(): Observable<boolean>;
}
