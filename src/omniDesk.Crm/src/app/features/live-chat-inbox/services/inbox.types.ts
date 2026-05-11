// Spec 007 US3 — types shared by the live-chat-inbox feature.

export type SenderType = 'visitor' | 'ai_agent' | 'attendant' | 'system';
export type ContentType = 'text' | 'image' | 'file' | 'system_event';

export interface ConversationSummary {
  id: string;
  visitor_id: string;
  department_id: string | null;
  attendant_id: string | null;
  last_message_at: string;
  created_at: string;
  channel: string;
}

export interface InboxMessage {
  id: string;
  sender_type: SenderType;
  sender_id: string | null;
  content_type: ContentType;
  content: string | null;
  attachment_url: string | null;
  attachment_name: string | null;
  attachment_size_bytes: number | null;
  created_at: string;
}

// Spec 008 US3 — status de entrega de mensagens WhatsApp.
export type WaDeliveryStatus = 'sent' | 'delivered' | 'read' | 'failed' | 'attachment_ready';

export interface WaMessageStatusPayload {
  conversation_id: string;
  message_id: string;
  wa_message_id: string;
  status: WaDeliveryStatus | string;
  timestamp: string;
  error_code?: string | null;
  error_message?: string | null;
  attachment_ready?: boolean;
  attachment_url?: string | null;
}

// Spec 008 US4 — eventos de janela 24h.
export interface WaSessionExpiringPayload {
  conversation_id: string;
  expires_at: string;
  minutes_remaining: number;
}

export interface WaSessionExpiredPayload {
  conversation_id: string;
  expired_at: string;
}

export type CrmEvent =
  | { type: 'chat.new_conversation'; payload: { conversation_id: string; department_id: string | null } }
  | { type: 'chat.message_received'; payload: InboxMessage & { conversation_id: string } }
  | { type: 'chat.visitor_typing'; payload: { conversation_id: string } }
  | { type: 'chat.conversation_resolved'; payload: { conversation_id: string; ended_by: string } }
  | { type: 'chat.browser_notify'; payload: { trigger: string; conversation_id: string; title: string; body: string } }
  | { type: 'wa.message_status'; payload: WaMessageStatusPayload }
  | { type: 'wa.session_expiring'; payload: WaSessionExpiringPayload }
  | { type: 'wa.session_expired'; payload: WaSessionExpiredPayload }
  | { type: 'ping'; ts: string };
