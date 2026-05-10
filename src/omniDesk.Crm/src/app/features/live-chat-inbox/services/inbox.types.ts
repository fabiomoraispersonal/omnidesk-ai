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

export type CrmEvent =
  | { type: 'chat.new_conversation'; payload: { conversation_id: string; department_id: string | null } }
  | { type: 'chat.message_received'; payload: InboxMessage & { conversation_id: string } }
  | { type: 'chat.visitor_typing'; payload: { conversation_id: string } }
  | { type: 'chat.conversation_resolved'; payload: { conversation_id: string; ended_by: string } }
  | { type: 'chat.browser_notify'; payload: { trigger: string; conversation_id: string; title: string; body: string } }
  | { type: 'ping'; ts: string };
