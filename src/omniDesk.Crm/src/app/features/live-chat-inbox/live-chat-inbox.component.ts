import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { InboxService } from './services/inbox.service';
import { CrmWebSocketService } from './services/crm-websocket.service';
import { BrowserNotificationService } from './services/browser-notification.service';
import {
  TemplatePickerDialogComponent,
  TemplatePickerResult,
} from './components/template-picker-dialog.component';
import { Router, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';

/**
 * Spec 007 US3 — multi-conversation inbox for attendants.
 *
 * Layout: left panel lists open conversations, right panel shows the selected
 * conversation's history + an input + a "Encerrar" button.
 *
 * On mount: load() the list, request notification permission, connect the WS.
 * On destroy: tear down the WS singleton.
 *
 * The two sub-components from tasks.md (T142 list / T143 detail) are consolidated
 * here for V1 — single-file keeps state ownership clean. The split is cosmetic.
 */
@Component({
  selector: 'app-live-chat-inbox',
  standalone: true,
  imports: [
    CommonModule, FormsModule, CardModule, ButtonModule,
    InputTextareaModule, ToastModule, DatePipe,
    TemplatePickerDialogComponent, RouterLink,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="inbox">
      <p-toast></p-toast>

      <aside class="list">
        <header><h2>Conversas</h2></header>
        <ul *ngIf="inbox.conversations().length; else empty">
          <li *ngFor="let conv of inbox.conversations()"
              [class.selected]="conv.id === inbox.selectedId()"
              (click)="select(conv.id)">
            <div class="meta">
              <span class="channel">{{ conv.channel }}</span>
              <span class="time">{{ conv.last_message_at | date: 'shortTime' }}</span>
            </div>
            <div class="visitor">Visitante {{ conv.visitor_id.slice(0, 8) }}</div>
            <div class="status">
              <span *ngIf="conv.attendant_id" class="badge owned">você</span>
              <span *ngIf="!conv.attendant_id" class="badge unassigned">novo</span>
            </div>
            <!-- Spec 009 T182 — link to ticket when conversation is linked -->
            <div *ngIf="conv.ticket_id" class="ticket-link-row" (click)="$event.stopPropagation()">
              <a [routerLink]="['/tickets', conv.ticket_id]" class="ticket-link">
                Abrir ticket TK...
              </a>
            </div>
          </li>
        </ul>
        <ng-template #empty>
          <p class="muted">Nenhuma conversa aberta no momento.</p>
        </ng-template>
      </aside>

      <main class="detail" *ngIf="inbox.selected() as conv; else placeholder">
        <header>
          <div>
            <h2>
              Visitante {{ conv.visitor_id.slice(0, 8) }}
              <!-- Spec 008 US3 — badge do canal WhatsApp -->
              <span *ngIf="conv.channel === 'whatsapp'" class="channel-badge whatsapp">WhatsApp</span>
              <span *ngIf="conv.channel === 'live_chat'" class="channel-badge live-chat">Web Chat</span>
            </h2>
            <p class="muted">{{ channelLabel(conv.channel) }} · iniciado em {{ conv.created_at | date: 'shortDate' }}</p>
          </div>
          <p-button label="Encerrar conversa" severity="danger" [outlined]="true"
                    icon="pi pi-times" (onClick)="resolve(conv.id)"></p-button>
        </header>

        <!-- Spec 008 US4 — banner janela 24h (WhatsApp only) -->
        <div class="session-banner" *ngIf="sessionWindowBanner() as banner"
             [class.warn]="banner.kind === 'warn'"
             [class.danger]="banner.kind === 'danger'">
          {{ banner.text }}
        </div>

        <div class="messages" #scroll>
          <div *ngFor="let msg of inbox.selectedMessages()"
               class="msg"
               [class.visitor]="msg.sender_type === 'visitor'"
               [class.attendant]="msg.sender_type === 'attendant'"
               [class.agent]="msg.sender_type === 'ai_agent'"
               [class.system]="msg.sender_type === 'system' || msg.content_type === 'system_event'">
            <div class="bubble">
              <ng-container *ngIf="msg.content_type === 'image' && msg.attachment_url">
                <img [src]="msg.attachment_url" [alt]="msg.attachment_name ?? ''" />
              </ng-container>
              <ng-container *ngIf="msg.content_type === 'file' && msg.attachment_url">
                <ng-container *ngIf="isAudioAttachment(msg.attachment_name); else fileLink">
                  <audio controls [src]="msg.attachment_url"></audio>
                </ng-container>
                <ng-template #fileLink>
                  <a [href]="msg.attachment_url" target="_blank" rel="noopener noreferrer">
                    📎 {{ msg.attachment_name ?? 'arquivo' }}
                  </a>
                </ng-template>
              </ng-container>
              <ng-container *ngIf="msg.content_type === 'file' && !msg.attachment_url">
                <span *ngIf="isAttachmentFailed(msg.attachment_name); else loadingMedia"
                      class="attachment-failed">⚠️ Falha ao carregar mídia</span>
                <ng-template #loadingMedia>
                  <span class="attachment-loading">⏳ Carregando mídia…</span>
                </ng-template>
              </ng-container>
              <ng-container *ngIf="msg.content_type !== 'image' && msg.content_type !== 'file'">
                {{ msg.content }}
              </ng-container>
              <small class="ts">
                {{ msg.created_at | date: 'short' }}
                <!-- Spec 008 US3 — ícones de delivery WhatsApp para mensagens enviadas (não-visitor) -->
                <ng-container *ngIf="isWhatsAppChannel() && msg.sender_type !== 'visitor' && deliveryIcon(msg.id) as ico">
                  <span class="delivery-icon"
                        [class.delivery-read]="ico.read"
                        [class.delivery-failed]="ico.failed"
                        [title]="ico.tooltip">{{ ico.glyph }}</span>
                </ng-container>
              </small>
            </div>
          </div>
        </div>

        <footer class="composer">
          <textarea pInputTextarea rows="2" [(ngModel)]="draft"
                    placeholder="Digite uma mensagem…"
                    [disabled]="isSessionExpired()"
                    (keydown.enter)="onEnter($event)"></textarea>
          @if (isSessionExpired()) {
            <p-button label="Selecionar template" icon="pi pi-list"
                      severity="warn"
                      (onClick)="openTemplatePicker()"></p-button>
          } @else {
            <p-button label="Enviar" icon="pi pi-send" (onClick)="send()" [disabled]="!draft().trim()"></p-button>
          }
        </footer>
      </main>

      <app-template-picker-dialog
        [visible]="templatePickerVisible()"
        [sending]="sendingTemplate()"
        (submitted)="onTemplateSubmitted($event)"
        (canceled)="closeTemplatePicker()"
      ></app-template-picker-dialog>

      <ng-template #placeholder>
        <main class="detail empty">
          <p class="muted">Selecione uma conversa à esquerda.</p>
        </main>
      </ng-template>
    </section>
  `,
  styles: [`
    .inbox { display: flex; height: calc(100vh - 64px); background: #F4F1EC; }
    .list { width: 320px; background: white; border-right: 1px solid #EDE7DF; overflow-y: auto; }
    .list header { padding: 16px; border-bottom: 1px solid #EDE7DF; }
    .list h2 { margin: 0; font-size: 16px; }
    .list ul { list-style: none; padding: 0; margin: 0; }
    .list li { padding: 12px 16px; border-bottom: 1px solid #EDE7DF; cursor: pointer; }
    .list li:hover { background: #F9F6F1; }
    .list li.selected { background: #EDE7DF; }
    .meta { display: flex; justify-content: space-between; font-size: 11px; color: #7A7A7A; margin-bottom: 4px; }
    .visitor { font-weight: 600; font-size: 14px; }
    .status .badge { font-size: 10px; padding: 2px 6px; border-radius: 999px; background: #EDE7DF; }
    .status .badge.unassigned { background: #C09A4D; color: white; }

    .detail { flex: 1; display: flex; flex-direction: column; }
    .detail header { padding: 16px; background: white; border-bottom: 1px solid #EDE7DF;
                     display: flex; justify-content: space-between; align-items: flex-start; }
    .detail header h2 { margin: 0; font-size: 16px; }
    .detail.empty { align-items: center; justify-content: center; }

    .messages { flex: 1; overflow-y: auto; padding: 16px; }
    .msg { display: flex; margin-bottom: 8px; }
    .msg .bubble { max-width: 70%; padding: 8px 12px; border-radius: 14px;
                   background: white; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .msg.visitor { justify-content: flex-end; }
    .msg.visitor .bubble { background: #6F7D5C; color: white; }
    .msg.system .bubble { background: transparent; color: #7A7A7A; font-style: italic; font-size: 12px; }
    .msg img { max-width: 100%; border-radius: 8px; }
    .msg .ts { display: block; font-size: 10px; color: #9A9A9A; margin-top: 4px; }

    .composer { display: flex; gap: 8px; padding: 12px; background: white; border-top: 1px solid #EDE7DF; }
    .composer textarea { flex: 1; resize: none; }
    .muted { color: #7A7A7A; }

    /* Spec 008 US3/US4/US6 — WhatsApp UI additions */
    .channel-badge { font-size: 11px; padding: 2px 8px; border-radius: 999px;
                     margin-left: 8px; vertical-align: middle; font-weight: 500; }
    .channel-badge.whatsapp { background: #25D366; color: white; }
    .channel-badge.live-chat { background: #6F7D5C; color: white; }
    .delivery-icon { margin-left: 6px; font-size: 11px; color: #9A9A9A; }
    .delivery-icon.delivery-read { color: #34B7F1; } /* WhatsApp blue */
    .delivery-icon.delivery-failed { color: #B85C5C; }
    .attachment-failed { color: #B85C5C; font-size: 12px; }
    .attachment-loading { color: #9A9A9A; font-size: 12px; font-style: italic; }
    .session-banner { padding: 8px 16px; font-size: 13px; text-align: center; }
    .session-banner.warn { background: #FDF7E2; color: #846200; border-bottom: 1px solid #F0E0A8; }
    .session-banner.danger { background: #FDECEC; color: #842020; border-bottom: 1px solid #F0A8A8; font-weight: 500; }
  `],
})
export class LiveChatInboxComponent implements OnInit, OnDestroy {
  protected readonly inbox = inject(InboxService);
  private readonly ws = inject(CrmWebSocketService);
  private readonly notify = inject(BrowserNotificationService);
  private readonly toast = inject(MessageService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly draft = signal('');
  protected readonly templatePickerVisible = signal(false);
  protected readonly sendingTemplate = signal(false);

  @ViewChild('scroll') private scrollEl?: ElementRef<HTMLDivElement>;

  async ngOnInit(): Promise<void> {
    await this.inbox.load();
    await this.notify.requestPermission();
    this.ws.connect();
  }

  ngOnDestroy(): void {
    this.ws.destroy();
  }

  protected async select(convId: string): Promise<void> {
    // Spec 009 T107 — when conversation has a linked ticket, navigate there instead.
    const conv = this.inbox.conversations().find((c) => c.id === convId);
    if (conv?.ticket_id) {
      void this.router.navigate(['/tickets', conv.ticket_id]);
      return;
    }
    await this.inbox.select(convId);
    requestAnimationFrame(() => this.scrollToBottom());
  }

  protected onEnter(e: Event): void {
    const ke = e as KeyboardEvent;
    if (ke.shiftKey) return;
    e.preventDefault();
    void this.send();
  }

  protected async send(): Promise<void> {
    const text = this.draft().trim();
    const id = this.inbox.selectedId();
    if (!text || !id) return;
    this.draft.set('');
    try {
      await this.inbox.send(id, text);
      requestAnimationFrame(() => this.scrollToBottom());
    } catch {
      this.toast.add({ severity: 'error', summary: 'Falha ao enviar mensagem.' });
    }
  }

  protected async resolve(id: string): Promise<void> {
    try {
      await this.inbox.resolve(id);
      this.toast.add({ severity: 'success', summary: 'Conversa encerrada.' });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Falha ao encerrar.' });
    }
  }

  private scrollToBottom(): void {
    const el = this.scrollEl?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }

  // ---- Spec 008 US3/US4/US6 helpers ----

  /** Conversa selecionada é WhatsApp? */
  protected isWhatsAppChannel(): boolean {
    return this.inbox.selected()?.channel === 'whatsapp';
  }

  /**
   * Mapeia status WhatsApp → glyph + tooltip + flag de cor especial (read=azul, failed=vermelho).
   * Retorna null se mensagem não tem status conhecido (ainda não chegou pelo WS).
   */
  protected deliveryIcon(messageId: string):
      { glyph: string; tooltip: string; read: boolean; failed: boolean } | null {
    const state = this.ws.waMessageStatuses().get(messageId);
    if (!state) return { glyph: '✓', tooltip: 'Enviado (aguardando confirmação)', read: false, failed: false };

    switch (state.status) {
      case 'sent':
        return { glyph: '✓', tooltip: 'Enviado para os servidores da Meta', read: false, failed: false };
      case 'delivered':
        return { glyph: '✓✓', tooltip: 'Entregue ao cliente', read: false, failed: false };
      case 'read':
        return { glyph: '✓✓', tooltip: 'Lido pelo cliente', read: true, failed: false };
      case 'failed':
        return {
          glyph: '✗',
          tooltip: state.errorMessage ?? 'Falha ao entregar a mensagem.',
          read: false,
          failed: true,
        };
      default:
        return { glyph: '✓', tooltip: state.status, read: false, failed: false };
    }
  }

  /** Banner do estado da janela 24h (WhatsApp only). */
  protected sessionWindowBanner(): { kind: 'warn' | 'danger'; text: string } | null {
    if (!this.isWhatsAppChannel()) return null;
    const id = this.inbox.selectedId();
    if (!id) return null;
    const w = this.ws.waSessionWindows().get(id);
    if (!w || w.status === 'active') return null;

    if (w.status === 'expiring') {
      return {
        kind: 'warn',
        text: `⚠️ A janela de 24h da Meta expira em ${w.minutesRemaining} min. Após isso, será necessário enviar template aprovado.`,
      };
    }
    return {
      kind: 'danger',
      text: '🚫 A janela de 24h da Meta expirou. Selecione um template aprovado para enviar.',
    };
  }

  /** Label amigável do canal. */
  protected channelLabel(channel: string): string {
    return channel === 'whatsapp' ? 'WhatsApp' : channel === 'live_chat' ? 'Web Chat' : channel;
  }

  /** Anexo de áudio (extensão típica)? */
  protected isAudioAttachment(name?: string | null): boolean {
    if (!name) return false;
    return /\.(ogg|opus|mp3|aac|m4a|wav)$/i.test(name);
  }

  /** Anexo marcado como falha pelo WaMediaDownloadJob? */
  protected isAttachmentFailed(name?: string | null): boolean {
    return !!name && name.startsWith('_failed:');
  }

  /** Janela 24h expirada na conversa atual? */
  protected isSessionExpired(): boolean {
    if (!this.isWhatsAppChannel()) return false;
    const id = this.inbox.selectedId();
    if (!id) return false;
    const w = this.ws.waSessionWindows().get(id);
    return w?.status === 'expired';
  }

  /** Abre dialog para escolher template aprovado (janela expirada). */
  protected openTemplatePicker(): void {
    this.templatePickerVisible.set(true);
  }

  protected closeTemplatePicker(): void {
    this.templatePickerVisible.set(false);
  }

  /**
   * Envia template via POST /api/whatsapp/send/template. Reuso do HttpClient
   * inline em vez de injetar o WhatsAppTemplatesService — apenas uma chamada.
   * No sucesso: limpa dialog, mensagem aparecerá via chat.message_received.
   */
  protected async onTemplateSubmitted(result: TemplatePickerResult): Promise<void> {
    const conversationId = this.inbox.selectedId();
    if (!conversationId) return;

    this.sendingTemplate.set(true);
    try {
      await firstValueFrom(
        this.http.post(`${environment.apiUrl}/api/whatsapp/send/template`, {
          conversation_id: conversationId,
          template_id: result.templateId,
          variables: result.variables,
        }),
      );
      this.toast.add({
        severity: 'success',
        summary: 'Template enviado',
        detail: 'Aguardando confirmação de entrega.',
        life: 3000,
      });
      this.closeTemplatePicker();
      requestAnimationFrame(() => this.scrollToBottom());
    } catch (err) {
      let detail = 'Falha ao enviar template.';
      if (err instanceof HttpErrorResponse && err.error?.error?.message) {
        detail = err.error.error.message;
      }
      this.toast.add({ severity: 'error', summary: 'Erro', detail, life: 5000 });
    } finally {
      this.sendingTemplate.set(false);
    }
  }
}
