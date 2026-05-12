// Spec 008 US5 — types compartilhados pela feature whatsapp-templates.

export type TemplateType =
  | 'appointment_reminder'
  | 'appointment_confirmation'
  | 'appointment_cancellation'
  | 'follow_up'
  | 'custom';

export type TemplateStatus = 'draft' | 'pending_meta' | 'approved' | 'rejected';

export type TemplateCategory = 'utility';

export interface WhatsAppTemplate {
  id: string;
  type: TemplateType;
  name: string;
  category: TemplateCategory;
  language: string;
  status: TemplateStatus;
  body_template: string;
  variable_labels: string[];
  variable_count: number;
  rejection_reason: string | null;
  submitted_at: string | null;
  approved_at: string | null;
  rejected_at: string | null;
  meta_template_id: string | null;
  created_at: string;
  updated_at: string;
}

export interface CreateTemplateRequest {
  type: TemplateType;
  name_suffix?: string | null;
  body_template: string;
  variable_labels: string[];
}

export interface UpdateTemplateRequest {
  body_template: string;
  variable_labels: string[];
}

export interface ListTemplatesFilter {
  status?: TemplateStatus;
  type?: TemplateType;
  page?: number;
  per_page?: number;
}

export interface ListTemplatesResult {
  items: WhatsAppTemplate[];
  total: number;
  page: number;
  per_page: number;
}

/**
 * Default body + label structure por tipo pré-definido (mirror server-side
 * <c>PredefinedTemplates</c>). Custom tem 0 variáveis fixas — tenant define.
 */
export interface PredefinedTemplateDefinition {
  defaultBody: string;
  variableLabels: readonly string[];
}

export const PREDEFINED_TEMPLATES: Readonly<Record<TemplateType, PredefinedTemplateDefinition>> = {
  appointment_reminder: {
    defaultBody:
      'Olá, {{1}}! Lembramos que você tem uma consulta agendada para {{2}} às {{3}}. ' +
      'Confirme com SIM ou cancele com NÃO.',
    variableLabels: ['nome do cliente', 'data da consulta', 'horário'],
  },
  appointment_confirmation: {
    defaultBody:
      'Olá, {{1}}! Seu agendamento para {{2}} às {{3}} foi confirmado. Até lá!',
    variableLabels: ['nome do cliente', 'data da consulta', 'horário'],
  },
  appointment_cancellation: {
    defaultBody:
      'Olá, {{1}}! Seu agendamento de {{2}} foi cancelado. Entre em contato para remarcar.',
    variableLabels: ['nome do cliente', 'data da consulta'],
  },
  follow_up: {
    defaultBody:
      'Olá, {{1}}! Seu atendimento foi encerrado. Ficou com alguma dúvida? Estamos à disposição.',
    variableLabels: ['nome do cliente'],
  },
  custom: {
    defaultBody: '',
    variableLabels: [],
  },
};

export function isPredefined(type: TemplateType): boolean {
  return type !== 'custom';
}

export const TYPE_LABEL: Readonly<Record<TemplateType, string>> = {
  appointment_reminder: 'Lembrete de Consulta',
  appointment_confirmation: 'Confirmação de Agendamento',
  appointment_cancellation: 'Cancelamento',
  follow_up: 'Follow-up',
  custom: 'Custom',
};

export const STATUS_LABEL: Readonly<Record<TemplateStatus, string>> = {
  draft: 'Rascunho',
  pending_meta: 'Aguardando Meta',
  approved: 'Aprovado',
  rejected: 'Rejeitado',
};
