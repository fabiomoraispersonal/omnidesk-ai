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
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { InboxService } from './services/inbox.service';
import { CrmWebSocketService } from './services/crm-websocket.service';
import { BrowserNotificationService } from './services/browser-notification.service';

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
          </li>
        </ul>
        <ng-template #empty>
          <p class="muted">Nenhuma conversa aberta no momento.</p>
        </ng-template>
      </aside>

      <main class="detail" *ngIf="inbox.selected() as conv; else placeholder">
        <header>
          <div>
            <h2>Visitante {{ conv.visitor_id.slice(0, 8) }}</h2>
            <p class="muted">{{ conv.channel }} · iniciado em {{ conv.created_at | date: 'shortDate' }}</p>
          </div>
          <p-button label="Encerrar conversa" severity="danger" [outlined]="true"
                    icon="pi pi-times" (onClick)="resolve(conv.id)"></p-button>
        </header>

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
                <a [href]="msg.attachment_url" target="_blank" rel="noopener noreferrer">
                  📎 {{ msg.attachment_name ?? 'arquivo' }}
                </a>
              </ng-container>
              <ng-container *ngIf="msg.content_type !== 'image' && msg.content_type !== 'file'">
                {{ msg.content }}
              </ng-container>
              <small class="ts">{{ msg.created_at | date: 'short' }}</small>
            </div>
          </div>
        </div>

        <footer class="composer">
          <textarea pInputTextarea rows="2" [(ngModel)]="draft"
                    placeholder="Digite uma mensagem…"
                    (keydown.enter)="onEnter($event)"></textarea>
          <p-button label="Enviar" icon="pi pi-send" (onClick)="send()" [disabled]="!draft().trim()"></p-button>
        </footer>
      </main>

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
  `],
})
export class LiveChatInboxComponent implements OnInit, OnDestroy {
  protected readonly inbox = inject(InboxService);
  private readonly ws = inject(CrmWebSocketService);
  private readonly notify = inject(BrowserNotificationService);
  private readonly toast = inject(MessageService);

  protected readonly draft = signal('');

  @ViewChild('scroll') private scrollEl?: ElementRef<HTMLDivElement>;

  async ngOnInit(): Promise<void> {
    await this.inbox.load();
    await this.notify.requestPermission();
    this.ws.connect();
  }

  ngOnDestroy(): void {
    this.ws.destroy();
  }

  protected async select(id: string): Promise<void> {
    await this.inbox.select(id);
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
}
