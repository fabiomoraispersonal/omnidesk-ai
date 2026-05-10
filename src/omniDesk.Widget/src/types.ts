// Spec 007 — Widget public + internal types.

export interface OmniDeskConfig {
  token: string;
  apiBaseUrl?: string;
  // Optional explicit override of the WS base URL — defaults to apiBaseUrl with ws/wss scheme.
  wsBaseUrl?: string;
}

export type WidgetPosition = 'bottom_right' | 'bottom_left';
export type LauncherIcon = 'chat' | 'message' | 'support';

export interface WidgetConfig {
  is_enabled: boolean;
  primary_color?: string;
  launcher_icon?: LauncherIcon;
  welcome_message?: string;
  input_placeholder?: string;
  position?: WidgetPosition;
  require_identification?: boolean;
  identification_fields?: ReadonlyArray<{
    field: 'name' | 'email' | 'phone';
    label: string;
    required: boolean;
  }>;
  privacy_policy_text?: string | null;
  privacy_policy_url?: string | null;
}

export interface InitResponse {
  tenant: { slug: string; company_name: string };
  config: WidgetConfig;
  active_conversation: ActiveConversation | null;
  disabled_message?: string;
}

export interface ActiveConversation {
  id: string;
  status: 'open' | 'resolved' | 'abandoned';
  has_attendant: boolean;
  lgpd_consent_at: string;
}

export interface StartConversationResponse {
  conversation_id: string;
  status: 'open';
  ws_url: string;
  ws_token: string;
  outcome: 'created' | 'resumed' | 'conflict';
}

export type SenderType = 'visitor' | 'ai_agent' | 'attendant' | 'system';
export type ContentType = 'text' | 'image' | 'file' | 'system_event';

export interface Message {
  id: string;
  conversation_id?: string;
  sender_type: SenderType;
  sender_id?: string | null;
  content_type: ContentType;
  content?: string | null;
  attachment_url?: string | null;
  attachment_name?: string | null;
  attachment_size_bytes?: number | null;
  created_at: string;
}

export type WsEvent =
  | { type: 'message.new'; payload: Message }
  | { type: 'agent.typing'; payload: { conversation_id: string } }
  | { type: 'conversation.assigned'; payload: { conversation_id: string; attendant_id: string } }
  | { type: 'conversation.resolved'; payload: { conversation_id: string; ended_by: string } }
  | { type: 'ping'; ts: string }
  | { type: 'error'; error: { code: string } }
  | { type: 'message.send.ack'; payload: { message_id: string; client_message_id: string; accepted: boolean; duplicate?: boolean } }
  | { type: 'messages.read.ack'; payload: { updated: number } }
  | { type: 'messages.replay.done'; payload: { count: number } };

export interface MessageSendPayload {
  client_message_id: string;
  content: string;
}

export interface ApiEnvelope<T> {
  success: true;
  data: T;
}

export interface ApiError {
  success: false;
  error: { code: string; message: string; details?: unknown };
}
