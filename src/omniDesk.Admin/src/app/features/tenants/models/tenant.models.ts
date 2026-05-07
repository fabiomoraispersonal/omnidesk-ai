export type TenantStatus = 'provisioning' | 'active' | 'blocked' | 'error';
export type ContactType = 'financial' | 'technical';
export type AgentType = 'orchestrator' | 'sub_agent';

export interface TenantContact {
  id: string;
  type: ContactType;
  name: string;
  email: string;
  phone: string;
}

export interface TenantMetricsSummary {
  postgres: { connected: boolean; error: string | null };
  redis: { connected: boolean; error: string | null };
  mongodb: { connected: boolean; error: string | null };
  conversations_last_30d: number;
  open_tickets: number;
  active_users: number;
}

export interface TenantMetricsDetail extends TenantMetricsSummary {
  postgres_schema_size_mb: number;
  redis_keys: number;
  redis_memory_mb: number;
  mongodb_documents: number;
  mongodb_size_mb: number;
  minio_objects: number;
  minio_size_mb: number;
  conversations_by_channel: { whatsapp: number; live_chat: number };
  tickets_by_status: Record<string, number>;
}

export interface TenantSummary {
  id: string;
  slug: string;
  razao_social: string;
  nome_fantasia: string | null;
  cnpj: string;
  status: TenantStatus;
  has_openai_key: boolean;
  created_at: string;
  blocked_at: string | null;
  metrics: TenantMetricsSummary | null;
}

export interface TenantDetail extends TenantSummary {
  openai_organization: string | null;
  openai_project: string | null;
  timezone: string;
  locale: string;
  currency: string;
  date_format: string;
  provisioning_error_log: string | null;
  contacts: TenantContact[];
  metrics: TenantMetricsDetail | null;
}

export interface ContactInput {
  name: string;
  email: string;
  phone: string;
}

export interface CreateTenantRequest {
  slug: string;
  razao_social: string;
  nome_fantasia?: string;
  cnpj: string;
  timezone: string;
  financial_contact: ContactInput;
  technical_contact: ContactInput;
  openai_api_key?: string;
  openai_organization?: string;
  openai_project?: string;
}

export interface UpdateTenantRequest {
  razao_social?: string;
  nome_fantasia?: string;
  timezone?: string;
  financial_contact?: ContactInput;
  technical_contact?: ContactInput;
  openai_api_key?: string;
  openai_organization?: string;
  openai_project?: string;
}

export interface CreateTenantResponse {
  id: string;
  slug: string;
  status: TenantStatus;
}

export interface ImpersonateResponse {
  impersonation_token: string;
  redirect_url: string;
  expires_at: string;
}

export interface AgentTemplate {
  id: string;
  name: string;
  type: AgentType;
  description: string;
  prompt: string | null;
  is_active: boolean;
  used_in_provisioning_count: number;
  created_at: string;
  updated_at: string;
}

export interface CreateAgentTemplateRequest {
  name: string;
  type: AgentType;
  description: string;
  prompt?: string;
}

export interface UpdateAgentTemplateRequest {
  name?: string;
  description?: string;
  prompt?: string;
  is_active?: boolean;
}
