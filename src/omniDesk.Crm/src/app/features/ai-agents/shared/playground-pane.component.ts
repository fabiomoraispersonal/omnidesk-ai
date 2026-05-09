import { ChangeDetectionStrategy, Component, Input, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { TextareaModule } from 'primeng/textarea';
import { environment } from '../../../../environments/environment';

interface PlaygroundResponse {
  success: boolean;
  data: {
    session_id: string;
    agent_id: string;
    agent_name: string;
    reply: string | null;
    tool_calls_observed: { tool: string; args: string }[];
    elapsed_ms: number;
    model: string;
    tokens: { input: number; output: number };
  };
}

@Component({
  selector: 'app-playground-pane',
  standalone: true,
  imports: [CommonModule, FormsModule, CardModule, ButtonModule, TextareaModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-card header="Testar agente" subheader="Mensagens aqui não criam conversas reais nem geram tickets.">
      <div class="messages" *ngIf="messages().length">
        <div *ngFor="let m of messages()" [class]="'msg msg-' + m.role">
          <div class="role">{{ m.role === 'user' ? 'Você' : agentName }}</div>
          <div class="content">{{ m.content }}</div>
          <div *ngIf="m.toolCalls?.length" class="tools">
            <small>Tool calls simuladas: {{ formatToolList(m.toolCalls!) }}</small>
          </div>
        </div>
      </div>
      <div class="input">
        <textarea pInputTextarea
                  [(ngModel)]="text"
                  rows="3"
                  placeholder="Digite uma mensagem de teste..."></textarea>
        <div class="actions">
          <p-button label="Limpar" severity="secondary" [text]="true"
                    [disabled]="!sessionId() || sending()"
                    (onClick)="clear()"></p-button>
          <p-button label="Enviar" icon="pi pi-send"
                    [loading]="sending()"
                    [disabled]="!text.trim()"
                    (onClick)="send()"></p-button>
        </div>
      </div>
    </p-card>
  `,
  styles: [`
    .messages { display: flex; flex-direction: column; gap: .5rem; max-height: 400px; overflow: auto; padding: .5rem; }
    .msg { padding: .5rem .75rem; border-radius: .5rem; }
    .msg-user { background: var(--surface-100); align-self: flex-end; max-width: 80%; }
    .msg-assistant { background: var(--surface-50); align-self: flex-start; max-width: 80%; }
    .role { font-size: .75rem; opacity: .7; margin-bottom: .25rem; }
    .tools { margin-top: .25rem; opacity: .65; }
    .input { display: flex; flex-direction: column; gap: .5rem; }
    .actions { display: flex; gap: .5rem; justify-content: flex-end; }
  `],
})
export class PlaygroundPaneComponent {
  private readonly http = inject(HttpClient);

  @Input({ required: true }) agentId!: string;
  @Input() agentName = 'Assistente';

  readonly sessionId = signal<string | null>(null);
  readonly sending = signal(false);
  readonly messages = signal<{ role: 'user' | 'assistant'; content: string; toolCalls?: { tool: string; args: string }[] }[]>([]);

  text = '';

  send(): void {
    const message = this.text.trim();
    if (!message || !this.agentId) return;
    this.sending.set(true);
    this.messages.update((arr) => [...arr, { role: 'user', content: message }]);
    this.text = '';

    this.http
      .post<PlaygroundResponse>(`${environment.apiUrl}/api/agents/${this.agentId}/test`, {
        message,
        sessionId: this.sessionId(),
      })
      .subscribe({
        next: (resp) => {
          this.sessionId.set(resp.data.session_id);
          this.messages.update((arr) => [
            ...arr,
            {
              role: 'assistant',
              content: resp.data.reply ?? '(sem resposta)',
              toolCalls: resp.data.tool_calls_observed,
            },
          ]);
          this.sending.set(false);
        },
        error: () => {
          this.messages.update((arr) => [
            ...arr,
            { role: 'assistant', content: '⚠️ Erro ao consultar o agente.' },
          ]);
          this.sending.set(false);
        },
      });
  }

  clear(): void {
    const sid = this.sessionId();
    if (sid) {
      this.http.delete(`${environment.apiUrl}/api/agents/playground-sessions/${sid}`).subscribe();
    }
    this.sessionId.set(null);
    this.messages.set([]);
  }

  formatToolList(calls: { tool: string; args: string }[]): string {
    return calls.map((c) => c.tool).join(', ');
  }
}
