export type AgentType = 'orchestrator' | 'sub_agent';

export interface AiAgentSummary {
  id: string;
  type: AgentType;
  name: string;
  short_description: string;
  model: string;
  department_id: string | null;
  department_name: string | null;
  is_active: boolean;
  openai_assistant_id_present: boolean;
  created_at: string;
  updated_at: string;
}

export interface AiAgentDetail extends AiAgentSummary {
  prompt: string;
  available_models_for_tenant: string[];
  deleted_at: string | null;
}

export interface CreateAiAgentRequest {
  name: string;
  short_description: string;
  prompt: string;
  model: string;
  department_id: string;
}

export interface UpdateAiAgentRequest {
  name?: string;
  short_description?: string;
  prompt?: string;
  model?: string;
  department_id?: string;
  is_active?: boolean;
}
