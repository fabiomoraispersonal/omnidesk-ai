import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { ToastModule } from 'primeng/toast';
import { SuggestionResponse, SuggestionService } from '../services/suggestion.service';

@Component({
  selector: 'omni-suggestion-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, TextareaModule, ToastModule],
  providers: [MessageService],
  templateUrl: './suggestion-panel.component.html',
})
export class SuggestionPanelComponent {
  private readonly service = inject(SuggestionService);
  private readonly toast = inject(MessageService);

  @Input({ required: true }) conversationId!: string;
  /** Emitted when the attendant approves/edits — the parent posts to the messages API. */
  @Output() approved = new EventEmitter<{ text: string; suggestionId: string; edited: boolean }>();

  protected readonly loading = signal(false);
  protected readonly suggestion = signal<SuggestionResponse | null>(null);
  protected editableText = '';
  protected hasEdited = false;

  request(): void {
    this.loading.set(true);
    this.suggestion.set(null);
    this.editableText = '';
    this.hasEdited = false;

    this.service.request(this.conversationId).subscribe({
      next: resp => {
        this.suggestion.set(resp);
        this.editableText = resp.text;
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        const code = err?.error?.error?.code;
        const message = err?.error?.error?.message
          ?? 'Não foi possível gerar sugestão agora. Tente novamente em alguns segundos.';
        this.toast.add({ severity: 'warn', summary: code ?? 'Erro', detail: message });
      },
    });
  }

  send(): void {
    const current = this.suggestion();
    if (!current) return;
    const action = this.hasEdited ? 'edited' : 'approved';
    // FR-038/SC-007: only the parent component is allowed to post the actual message.
    // We emit the approved text and record the human action audit entry.
    this.approved.emit({ text: this.editableText, suggestionId: current.suggestionId, edited: this.hasEdited });
    this.service.recordAction(this.conversationId, current.suggestionId, action,
      this.hasEdited ? this.editableText : undefined).subscribe();
    this.suggestion.set(null);
  }

  discard(): void {
    const current = this.suggestion();
    if (!current) return;
    this.service.recordAction(this.conversationId, current.suggestionId, 'discarded').subscribe();
    this.suggestion.set(null);
  }

  onEdit(): void { this.hasEdited = true; }
}
