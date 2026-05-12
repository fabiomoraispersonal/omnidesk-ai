// Spec 009 US2/US4 — Right-side panel showing ticket metadata + action buttons.
import {
  ChangeDetectionStrategy,
  Component,
  ViewChild,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { TicketDetail, TicketPriority, TicketStatus, TicketsService } from '../../tickets-kanban/services/tickets.service';
import { SlaBadgeComponent } from '../../tickets-kanban/components/sla-badge.component';
import { InlineStatusEditorComponent } from './inline-status-editor.component';
import { InlinePriorityEditorComponent } from './inline-priority-editor.component';
import { TagsEditorComponent } from './tags-editor.component';
import { TransferDialogComponent, TransferPayload } from './transfer-dialog.component';

@Component({
  selector: 'app-ticket-side-panel',
  standalone: true,
  imports: [
    CommonModule,
    DatePipe,
    RouterLink,
    ButtonModule,
    ConfirmDialogModule,
    ToastModule,
    SlaBadgeComponent,
    InlineStatusEditorComponent,
    InlinePriorityEditorComponent,
    TagsEditorComponent,
    TransferDialogComponent,
  ],
  providers: [ConfirmationService, MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-confirmDialog></p-confirmDialog>
    <p-toast position="bottom-right"></p-toast>

    <aside class="side-panel">
      <!-- Protocol -->
      <div class="panel-row">
        <span class="field-label">Protocolo</span>
        <span class="field-value mono">{{ ticket().protocol }}</span>
      </div>

      <!-- Status editor -->
      <div class="panel-row">
        <app-inline-status-editor
          [currentStatus]="ticket().status"
          (statusChange)="onStatusChange($event)"
        />
      </div>

      <!-- Priority editor -->
      <div class="panel-row">
        <app-inline-priority-editor
          [currentPriority]="ticket().priority"
          (priorityChange)="onPriorityChange($event)"
        />
      </div>

      <!-- Tags editor -->
      <div class="panel-row">
        <app-tags-editor
          [currentTags]="ticket().tags"
          (tagsChange)="onTagsChange($event)"
        />
      </div>

      <!-- SLA -->
      @if (ticket().sla) {
        <div class="panel-row">
          <span class="field-label">SLA</span>
          <app-sla-badge [sla]="ticket().sla" />
        </div>
      }

      <hr class="divider" />

      <!-- Department -->
      <div class="panel-row">
        <span class="field-label">Departamento</span>
        <span class="field-value">{{ ticket().department?.name ?? '—' }}</span>
      </div>

      <!-- Attendant -->
      <div class="panel-row">
        <span class="field-label">Atendente</span>
        <span class="field-value">{{ ticket().attendant?.name ?? 'Sem atendente' }}</span>
      </div>

      <!-- Contact -->
      @if (ticket().contact) {
        <div class="panel-row">
          <span class="field-label">Contato</span>
          <a
            class="field-value contact-link"
            [routerLink]="['/contacts', ticket().contact!.id]"
          >
            {{ ticket().contact!.name }}
          </a>
        </div>
        @if (ticket().contact!.email) {
          <div class="panel-row">
            <span class="field-label">E-mail</span>
            <span class="field-value">{{ ticket().contact!.email }}</span>
          </div>
        }
      }

      <!-- Created / Updated -->
      <div class="panel-row">
        <span class="field-label">Criado em</span>
        <time class="field-value" [dateTime]="ticket().created_at">
          {{ ticket().created_at | date:'short' }}
        </time>
      </div>

      <hr class="divider" />

      <!-- Transfer dialog -->
      <app-transfer-dialog
        #transferDialog
        (confirmed)="onTransferConfirmed($event)"
      ></app-transfer-dialog>

      <!-- Action buttons -->
      <div class="action-buttons">
        <button
          pButton
          label="Transferir"
          class="p-button-outlined p-button-sm"
          icon="pi pi-arrow-right-arrow-left"
          [disabled]="acting()"
          (click)="openTransferDialog()"
        ></button>
        <button
          pButton
          label="Encerrar"
          class="p-button-success p-button-sm"
          icon="pi pi-check"
          [disabled]="acting()"
          (click)="confirmResolve()"
        ></button>
        <button
          pButton
          label="Cancelar"
          class="p-button-danger p-button-outlined p-button-sm"
          icon="pi pi-times"
          [disabled]="acting()"
          (click)="confirmCancel()"
        ></button>
      </div>
    </aside>
  `,
  styles: [`
    .side-panel {
      display: flex;
      flex-direction: column;
      gap: 10px;
      padding: 16px;
      height: 100%;
      overflow-y: auto;
      background: #fff;
      border-left: 1px solid #e0e0e0;
    }

    .panel-row {
      display: flex;
      flex-direction: column;
      gap: 3px;
    }

    .field-label {
      font-size: 11px;
      font-weight: 600;
      color: #7A7A7A;
      text-transform: uppercase;
      letter-spacing: 0.4px;
    }
    .field-value {
      font-size: 13px;
      color: #2F2F2F;
    }
    .mono { font-family: monospace; font-size: 12px; }

    .contact-link {
      color: #6F7D5C;
      text-decoration: none;
      font-weight: 600;
    }
    .contact-link:hover { text-decoration: underline; }

    .divider {
      border: none;
      border-top: 1px solid #EDE7DF;
      margin: 4px 0;
    }

    .action-buttons {
      display: flex;
      flex-direction: column;
      gap: 8px;
      margin-top: auto;
      padding-top: 12px;
    }
  `],
})
export class TicketSidePanelComponent {
  private readonly ticketsService = inject(TicketsService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);

  @ViewChild('transferDialog') transferDialog!: TransferDialogComponent;

  readonly ticket = input.required<TicketDetail>();
  readonly resolved = output<void>();
  readonly cancelled = output<void>();
  readonly transferred = output<void>();
  readonly statusChanged = output<TicketStatus>();

  readonly acting = signal(false);

  openTransferDialog(): void {
    this.transferDialog.open();
  }

  async onTransferConfirmed(payload: TransferPayload): Promise<void> {
    this.acting.set(true);
    try {
      await this.ticketsService.transfer(this.ticket().id, payload);
      this.messageService.add({ severity: 'success', summary: 'Ticket transferido', life: 3000 });
      this.transferred.emit();
    } catch {
      this.messageService.add({ severity: 'error', summary: 'Erro ao transferir ticket', life: 4000 });
    } finally {
      this.acting.set(false);
    }
  }

  async onStatusChange(status: TicketStatus): Promise<void> {
    try {
      await this.ticketsService.patchStatus(this.ticket().id, status);
      this.statusChanged.emit(status);
    } catch {
      this.messageService.add({ severity: 'error', summary: 'Erro ao atualizar status', life: 4000 });
    }
  }

  async onPriorityChange(priority: TicketPriority): Promise<void> {
    try {
      await this.ticketsService.patchStatus(this.ticket().id, this.ticket().status);
      // Note: priority PATCH is via general PATCH /api/tickets/{id} — placeholder uses status patch
      // TODO: wire to PATCH /api/tickets/{id} with { priority } when endpoint is available
    } catch {
      this.messageService.add({ severity: 'error', summary: 'Erro ao atualizar prioridade', life: 4000 });
    }
  }

  onTagsChange(_tags: string[]): void {
    // TODO: wire to PATCH /api/tickets/{id} with { tags } when endpoint is available
  }

  confirmResolve(): void {
    this.confirmationService.confirm({
      message: 'Deseja encerrar este ticket?',
      header: 'Encerrar Ticket',
      icon: 'pi pi-check-circle',
      acceptLabel: 'Encerrar',
      rejectLabel: 'Cancelar',
      accept: () => void this.doResolve(),
    });
  }

  confirmCancel(): void {
    this.confirmationService.confirm({
      message: 'Deseja cancelar este ticket? Esta ação não pode ser desfeita.',
      header: 'Cancelar Ticket',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Sim, cancelar',
      rejectLabel: 'Voltar',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => void this.doCancel(),
    });
  }

  private async doResolve(): Promise<void> {
    this.acting.set(true);
    try {
      await this.ticketsService.resolve(this.ticket().id);
      this.resolved.emit();
    } catch {
      this.messageService.add({ severity: 'error', summary: 'Erro ao encerrar ticket', life: 4000 });
    } finally {
      this.acting.set(false);
    }
  }

  private async doCancel(): Promise<void> {
    this.acting.set(true);
    try {
      await this.ticketsService.cancel(this.ticket().id);
      this.cancelled.emit();
    } catch {
      this.messageService.add({ severity: 'error', summary: 'Erro ao cancelar ticket', life: 4000 });
    } finally {
      this.acting.set(false);
    }
  }
}
