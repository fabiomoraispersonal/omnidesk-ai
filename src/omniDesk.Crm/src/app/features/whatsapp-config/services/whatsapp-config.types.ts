// Spec 008 US2 — types compartilhados pela feature whatsapp-config.

export type WhatsAppChannelStatus = 'not_configured' | 'configured_inactive' | 'active';

export interface WhatsAppConfig {
  is_enabled: boolean;
  phone_number: string | null;
  display_name: string | null;
  waba_id: string | null;
  phone_number_id: string | null;
  access_token_configured: boolean;
  app_secret_configured: boolean;
  webhook_verify_token: string;
  webhook_url: string;
  business_hours_enabled: boolean;
  channel_status: WhatsAppChannelStatus;
  updated_at: string;
}

export interface UpdateWhatsAppConfigRequest {
  phone_number?: string | null;
  display_name?: string | null;
  waba_id?: string | null;
  phone_number_id?: string | null;
  /** Vazio = manter o valor existente. */
  access_token?: string;
  /** Vazio = manter o valor existente. */
  app_secret?: string;
  business_hours_enabled?: boolean;
}

export interface ToggleChannelResult {
  is_enabled: boolean;
  channel_status: WhatsAppChannelStatus;
}

export type ErrorCode =
  | 'VALIDATION_ERROR'
  | 'WHATSAPP_CONFIG_NOT_FOUND'
  | 'WHATSAPP_NOT_CONFIGURED'
  | 'INVALID_TOKEN'
  | 'FORBIDDEN';

export interface ApiError {
  code: ErrorCode | string;
  message: string;
  details?: { field?: string; code?: string; message?: string }[];
}
