// Spec 007 US2 — types shared by the live-chat-config feature.

export type LauncherIcon = 'chat' | 'message' | 'support';
export type WidgetPosition = 'bottom_right' | 'bottom_left';
export type IdentificationFieldKey = 'name' | 'email' | 'phone';

export interface IdentificationField {
  field: IdentificationFieldKey;
  label: string;
  required: boolean;
}

export interface WidgetConfig {
  is_enabled: boolean;
  primary_color: string;
  launcher_icon: LauncherIcon;
  company_name: string;
  welcome_message: string;
  input_placeholder: string | null;
  position: WidgetPosition;
  require_identification: boolean;
  identification_fields: IdentificationField[] | null;
  allowed_domains: string[] | null;
  privacy_policy_text: string | null;
  privacy_policy_url: string | null;
  abandonment_timeout_hours: number;
  inactivity_close_hours: number;
  updated_at: string;
}

export interface WidgetConfigSnapshot {
  widget_token: string;
  installation_snippet: string;
  config: WidgetConfig;
}

export interface ToggleResult {
  is_enabled: boolean;
  affected_conversations: number;
}
