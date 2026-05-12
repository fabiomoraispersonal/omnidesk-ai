// Spec 009 US2 — Collapsible internal notes section.
import {
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { TicketNote } from '../../tickets-kanban/services/tickets.service';
import { TicketDetailService } from '../services/ticket-detail.service';

@Component({
  selector: 'app-internal-notes-section',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, ButtonModule, InputTextareaModule, ToastModule],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-toast position="bottom-right"></p-toast>

    <section class="notes-section">
      <!-- Collapsible header -->
      <button class="notes-header" (click)="toggleCollapsed()" [attr.aria-expanded]="!collapsed()">
        <span class="lock-icon">&#128274;</span>
        <span class="header-text">Anotações internas — não visíveis ao cliente</span>
        <span class="chevron" [class.open]="!collapsed()">&#8250;</span>
      </button>

      @if (!collapsed()) {
        <div class="notes-body">
          <!-- List -->
          @for (note of notes(); track note.id) {
            <div class="note-item">
              <div class="note-meta">
                <span class="note-author">{{ note.attendant_name }}</span>
                <time class="note-time" [dateTime]="note.created_at">
                  {{ note.created_at | date:'short' }}
                </time>
              </div>
              <p class="note-content">{{ note.content }}</p>
            </div>
          } @empty {
            <p class="no-notes">Nenhuma anotação ainda.</p>
          }

          <!-- Add note -->
          <div class="note-form">
            <textarea
              pInputTextarea
              [(ngModel)]="newNoteContent"
              placeholder="Escreva uma anotação interna..."
              rows="3"
              [autoResize]="true"
              class="note-textarea"
            ></textarea>
            <button
              pButton
              label="Adicionar"
              class="p-button-sm"
              [disabled]="saving() || !newNoteContent.trim()"
              (click)="addNote()"
            ></button>
          </div>
        </div>
      }
    </section>
  `,
  styles: [`
    .notes-section {
      border: 1px solid #e0d8cd;
      border-radius: 8px;
      overflow: hidden;
      background: #fdf8f2;
    }

    .notes-header {
      width: 100%;
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 14px;
      background: #f5ede0;
      border: none;
      cursor: pointer;
      font-size: 13px;
      font-weight: 600;
      color: #4A563E;
      text-align: left;
    }
    .lock-icon { font-size: 14px; }
    .header-text { flex: 1; }
    .chevron { font-size: 18px; transform: rotate(90deg); transition: transform 0.2s; }
    .chevron.open { transform: rotate(-90deg); }

    .notes-body {
      padding: 12px 14px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .note-item {
      padding: 8px 10px;
      background: #fff;
      border-radius: 6px;
      border-left: 3px solid #C09A4D;
    }
    .note-meta {
      display: flex;
      justify-content: space-between;
      margin-bottom: 4px;
    }
    .note-author { font-size: 11px; font-weight: 700; color: #4A563E; }
    .note-time   { font-size: 10px; color: #7A7A7A; }
    .note-content { margin: 0; font-size: 13px; color: #2F2F2F; white-space: pre-wrap; }

    .no-notes { color: #7A7A7A; font-size: 12px; text-align: center; padding: 8px; margin: 0; }

    .note-form {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .note-textarea { width: 100%; resize: vertical; }
  `],
})
export class InternalNotesSectionComponent {
  private readonly detailService = inject(TicketDetailService);
  private readonly messageService = inject(MessageService);

  readonly notes = input<TicketNote[]>([]);
  readonly ticketId = input.required<string>();
  readonly noteAdded = output<TicketNote>();

  readonly collapsed = signal(false);
  readonly saving = signal(false);
  newNoteContent = '';

  toggleCollapsed(): void {
    this.collapsed.update((v) => !v);
  }

  async addNote(): Promise<void> {
    const content = this.newNoteContent.trim();
    if (!content || this.saving()) return;

    this.saving.set(true);
    try {
      const note = await this.detailService.addNote(this.ticketId(), content);
      this.newNoteContent = '';
      this.noteAdded.emit(note);
    } catch {
      this.messageService.add({
        severity: 'error',
        summary: 'Erro',
        detail: 'Não foi possível salvar a anotação.',
        life: 4000,
      });
    } finally {
      this.saving.set(false);
    }
  }
}
