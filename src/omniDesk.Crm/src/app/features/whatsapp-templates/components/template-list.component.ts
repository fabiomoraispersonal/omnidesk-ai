import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { RejectionReasonComponent } from './rejection-reason.component';
import {
  STATUS_LABEL,
  TYPE_LABEL,
  TemplateStatus,
  WhatsAppTemplate,
} from '../services/whatsapp-templates.types';

/**
 * Spec 008 US5 — lista de templates como cards. Cada card mostra:
 * tipo, name, badge de status, body preview, ações por status
 * (draft → Editar/Excluir/Submeter; pending → none; approved → Excluir N/A;
 * rejected → Excluir + motivo).
 */
@Component({
  selector: 'app-template-list',
  standalone: true,
  imports: [CommonModule, TagModule, ButtonModule, CardModule, RejectionReasonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (items().length === 0) {
      <p class="muted">Nenhum template cadastrado ainda. Clique em "Novo template" para começar.</p>
    } @else {
      <div class="grid">
        @for (t of items(); track t.id) {
          <article class="card">
            <header>
              <div>
                <p class="type">{{ typeLabel(t.type) }}</p>
                <h3>{{ t.name }}</h3>
              </div>
              <p-tag [severity]="severity(t.status)" [value]="statusLabel(t.status)"></p-tag>
            </header>

            <pre class="body">{{ t.body_template }}</pre>

            @if (t.status === 'rejected') {
              <app-rejection-reason [reason]="t.rejection_reason"></app-rejection-reason>
            }

            @if (t.variable_labels.length > 0) {
              <ul class="variables">
                @for (label of t.variable_labels; track $index) {
                  <li>
                    <code>&#123;&#123;{{ $index + 1 }}&#125;&#125;</code>
                    <span>{{ label }}</span>
                  </li>
                }
              </ul>
            }

            <footer class="actions">
              @if (canEdit(t.status) && !readOnly()) {
                <button pButton type="button" icon="pi pi-pencil"
                        severity="secondary" label="Editar"
                        (click)="edit.emit(t)"></button>
              }
              @if (canSubmit(t.status) && !readOnly()) {
                <button pButton type="button" icon="pi pi-send"
                        label="Submeter à Meta"
                        (click)="submit.emit(t)"></button>
              }
              @if (canDelete(t.status) && !readOnly()) {
                <button pButton type="button" icon="pi pi-trash"
                        severity="danger" [outlined]="true"
                        (click)="remove.emit(t)"
                        aria-label="Excluir template"></button>
              }
            </footer>
          </article>
        }
      </div>
    }
  `,
  styles: [`
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
      gap: 1rem;
    }
    .card {
      background: white;
      border: 1px solid #EDE7DF;
      border-radius: 8px;
      padding: 1rem;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 1rem;
    }
    .type {
      margin: 0 0 0.25rem 0;
      font-size: 0.75rem;
      text-transform: uppercase;
      color: #7A7A7A;
      letter-spacing: 0.5px;
    }
    h3 {
      margin: 0;
      font-size: 0.95rem;
      font-family: var(--font-family-mono, monospace);
      word-break: break-all;
    }
    .body {
      margin: 0;
      padding: 0.5rem;
      background: #F4F1EC;
      border-radius: 4px;
      font-size: 0.875rem;
      white-space: pre-wrap;
      font-family: var(--font-family-base);
    }
    .variables {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.8rem;
    }
    .variables li {
      display: flex;
      gap: 0.5rem;
      color: #7A7A7A;
    }
    .variables code {
      background: #EDE7DF;
      padding: 0 0.25rem;
      border-radius: 3px;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
      justify-content: flex-end;
      margin-top: auto;
    }
    .muted { color: #7A7A7A; }
  `],
})
export class TemplateListComponent {
  readonly items = input.required<WhatsAppTemplate[]>();
  readonly readOnly = input(false);

  readonly edit = output<WhatsAppTemplate>();
  readonly submit = output<WhatsAppTemplate>();
  readonly remove = output<WhatsAppTemplate>();

  protected typeLabel(t: string): string {
    return TYPE_LABEL[t as keyof typeof TYPE_LABEL] ?? t;
  }

  protected statusLabel(s: TemplateStatus): string {
    return STATUS_LABEL[s];
  }

  protected severity(s: TemplateStatus): 'secondary' | 'info' | 'success' | 'danger' {
    switch (s) {
      case 'draft':        return 'secondary';
      case 'pending_meta': return 'info';
      case 'approved':     return 'success';
      case 'rejected':     return 'danger';
    }
  }

  protected canEdit(s: TemplateStatus): boolean   { return s === 'draft'; }
  protected canDelete(s: TemplateStatus): boolean { return s === 'draft' || s === 'rejected'; }
  protected canSubmit(s: TemplateStatus): boolean { return s === 'draft'; }
}
