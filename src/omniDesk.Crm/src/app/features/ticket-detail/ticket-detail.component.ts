// Spec 009 US2 — Ticket detail page: 2-panel layout (timeline + side panel).
import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { TicketDetailService } from './services/ticket-detail.service';
import { TicketsService } from '../tickets-kanban/services/tickets.service';
import { CrmWebSocketService } from '../live-chat-inbox/services/crm-websocket.service';
import { SendTemplateModalComponent } from '../whatsapp-templates/send-template-modal.component';
import { ConversationTimelineComponent } from '../../shared/components/conversation-timeline/conversation-timeline.component';
import { InternalNotesSectionComponent } from './components/internal-notes-section.component';
import { TicketSidePanelComponent } from './components/ticket-side-panel.component';

@Component({
  selector: 'app-ticket-detail',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    InputTextareaModule,
    ToastModule,
    ConversationTimelineComponent,
    InternalNotesSectionComponent,
    TicketSidePanelComponent,
    SendTemplateModalComponent,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-toast></p-toast>

    @if (detailService.loading()) {
      <div class="detail-loading" aria-busy="true">
        <div class="shimmer skeleton-header"></div>
        <div class="detail-panels">
          <div class="shimmer skeleton-panel"></div>
          <div class="shimmer skeleton-side"></div>
        </div>
      </div>
    } @else if (detailService.detail(); as ticket) {
      <div class="detail-page">
        <!-- Page header -->
        <header class="detail-header">
          <button
            pButton
            icon="pi pi-arrow-left"
            class="p-button-text p-button-sm"
            (click)="goBack()"
            aria-label="Voltar ao Kanban"
          ></button>
          <h1 class="detail-title">{{ ticket.protocol }}</h1>
          <span class="detail-subject">{{ ticket.subject }}</span>
        </header>

        <!-- 2-panel layout -->
        <div class="detail-panels">
          <!-- Left panel: conversation + notes + reply -->
          <section class="panel-left">
            <app-conversation-timeline [messages]="ticket.conversation" />

            <!-- Reply area (placeholder — full send in US4/US5) -->
            <div class="reply-area">
              <textarea
                pInputTextarea
                [(ngModel)]="replyContent"
                placeholder="Responder ao cliente... (em breve)"
                rows="3"
                disabled
                class="reply-textarea"
              ></textarea>
              <div class="reply-actions">
                <button
                  pButton
                  type="button"
                  icon="pi pi-send"
                  label="Enviar template"
                  class="p-button-sm p-button-outlined"
                  (click)="openTemplateModal()"
                ></button>
              </div>
            </div>

            <!-- Internal notes -->
            <app-internal-notes-section
              [notes]="ticket.notes"
              [ticketId]="ticket.id"
            />
          </section>

          <!-- Right panel: metadata + actions -->
          <app-ticket-side-panel
            [ticket]="ticket"
            (resolved)="onResolved()"
            (cancelled)="onCancelled()"
            class="panel-right"
          />
        </div>
      </div>
    } @else {
      <div class="detail-error">
        <p>Ticket não encontrado.</p>
        <button pButton label="Voltar" class="p-button-text" (click)="goBack()"></button>
      </div>
    }

    @if (templateModalOpen() && currentTicketId()) {
      <app-send-template-modal
        [ticketId]="currentTicketId()!"
        [visible]="templateModalOpen()"
        (visibleChange)="templateModalOpen.set($event)"
        (sent)="onTemplateSent()"
      />
    }
  `,
  styles: [`
    .detail-page {
      display: flex;
      flex-direction: column;
      height: 100%;
      overflow: hidden;
    }

    /* Loading skeleton */
    .detail-loading { padding: 20px; display: flex; flex-direction: column; gap: 16px; height: 100%; }
    .skeleton-header { height: 56px; border-radius: 8px; }
    .skeleton-panel  { flex: 1; border-radius: 8px; }
    .skeleton-side   { width: 300px; border-radius: 8px; }

    @keyframes shimmer {
      0%   { background-position: -800px 0; }
      100% { background-position: 800px 0; }
    }
    .shimmer {
      background: linear-gradient(90deg, #EDE7DF 25%, #F4F1EC 50%, #EDE7DF 75%);
      background-size: 1600px 100%;
      animation: shimmer 1.4s infinite linear;
    }

    /* Header */
    .detail-header {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 20px;
      border-bottom: 1px solid #e0e0e0;
      background: #fff;
      flex-shrink: 0;
    }
    .detail-title {
      font-size: 16px;
      font-weight: 700;
      color: #2F2F2F;
      margin: 0;
      font-family: monospace;
    }
    .detail-subject {
      font-size: 13px;
      color: #7A7A7A;
      flex: 1;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* Panels */
    .detail-panels {
      display: flex;
      flex: 1;
      overflow: hidden;
      gap: 0;
    }

    .panel-left {
      flex: 1;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      min-width: 0;
    }

    .panel-right {
      width: 320px;
      flex-shrink: 0;
    }

    .reply-area {
      padding: 8px 16px;
      border-top: 1px solid #EDE7DF;
      border-bottom: 1px solid #EDE7DF;
      background: #F4F1EC;
      flex-shrink: 0;
    }
    .reply-textarea { width: 100%; }

    .detail-error {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      height: 100%;
      gap: 12px;
      color: #7A7A7A;
    }
  `],
})
export class TicketDetailComponent implements OnInit, OnDestroy {
  readonly detailService = inject(TicketDetailService);
  private readonly ticketsService = inject(TicketsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);
  private readonly ws = inject(CrmWebSocketService);

  // Spec 010 US2 T063 — silence-rule heartbeat. TTL 60s server-side, refresh every 30s.
  private viewingTicketId: string | null = null;
  private viewingTimer: ReturnType<typeof setInterval> | null = null;

  // Spec 010 US5 — manual template send modal state.
  protected readonly templateModalOpen = signal(false);
  protected readonly currentTicketId = computed(
    () => this.detailService.detail()?.id ?? null);

  replyContent = '';

  openTemplateModal(): void {
    if (this.currentTicketId()) this.templateModalOpen.set(true);
  }

  onTemplateSent(): void {
    // Reload the ticket so the new message appears in the timeline.
    const id = this.currentTicketId();
    if (id) void this.detailService.load(id);
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) void this.detailService.load(id);

    // Spec 010 US2 T063 — tell backend which ticket we're viewing so it can
    // suppress browser push for new_message / client_replied on THIS ticket.
    if (id) {
      this.viewingTicketId = id;
      this.ws.connect(); // idempotent
      this.ws.send({ type: 'attendant.viewing_ticket', ticket_id: id });
      this.viewingTimer = setInterval(() => {
        if (this.viewingTicketId) {
          this.ws.send({ type: 'attendant.viewing_ticket', ticket_id: this.viewingTicketId });
        }
      }, 30_000);
    }
  }

  ngOnDestroy(): void {
    if (this.viewingTimer) {
      clearInterval(this.viewingTimer);
      this.viewingTimer = null;
    }
    if (this.viewingTicketId) {
      this.ws.send({ type: 'attendant.viewing_ticket', ticket_id: null });
      this.viewingTicketId = null;
    }
  }

  goBack(): void {
    void this.router.navigate(['/kanban']);
  }

  onResolved(): void {
    this.messageService.add({
      severity: 'success',
      summary: 'Ticket encerrado',
      detail: 'O ticket foi resolvido com sucesso.',
      life: 3000,
    });
    setTimeout(() => void this.router.navigate(['/kanban']), 1500);
  }

  onCancelled(): void {
    this.messageService.add({
      severity: 'info',
      summary: 'Ticket cancelado',
      life: 3000,
    });
    setTimeout(() => void this.router.navigate(['/kanban']), 1500);
  }
}
