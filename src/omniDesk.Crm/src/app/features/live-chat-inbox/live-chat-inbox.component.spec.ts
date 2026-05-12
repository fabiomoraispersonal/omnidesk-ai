import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { LiveChatInboxComponent } from './live-chat-inbox.component';
import { InboxService } from './services/inbox.service';
import {
  CrmWebSocketService,
  WaMessageStatusState,
  WaSessionWindowState,
} from './services/crm-websocket.service';
import { BrowserNotificationService } from './services/browser-notification.service';
import { ConversationSummary, InboxMessage, WaDeliveryStatus } from './services/inbox.types';

/**
 * Spec 008 T089 — testes das extensões WhatsApp da inbox:
 *  - badge "WhatsApp" no header da conversa
 *  - ícones de delivery (✓ / ✓✓ / ✓✓ azul / ✗)
 *  - banner janela 24h (expiring/expired)
 *  - audio attachment via player
 *  - placeholder de mídia carregando / falhada
 */
describe('LiveChatInboxComponent — Spec 008 extensions', () => {
  function buildConv(overrides: Partial<ConversationSummary> = {}): ConversationSummary {
    return {
      id: 'conv-1',
      visitor_id: '11111111-1111-1111-1111-111111111111',
      department_id: null,
      attendant_id: null,
      last_message_at: '2026-05-10T12:00:00Z',
      created_at: '2026-05-10T11:00:00Z',
      channel: 'whatsapp',
      ...overrides,
    };
  }

  function buildMessage(overrides: Partial<InboxMessage> = {}): InboxMessage {
    return {
      id: 'msg-1',
      sender_type: 'attendant',
      sender_id: null,
      content_type: 'text',
      content: 'Olá',
      attachment_url: null,
      attachment_name: null,
      attachment_size_bytes: null,
      created_at: '2026-05-10T12:00:01Z',
      ...overrides,
    };
  }

  function setupHarness(opts: {
    conv?: ConversationSummary;
    messages?: InboxMessage[];
    waStatuses?: ReadonlyMap<string, WaMessageStatusState>;
    waWindow?: ReadonlyMap<string, WaSessionWindowState>;
  } = {}) {
    const conv = opts.conv ?? buildConv();
    const inboxStub = {
      conversations: signal([conv]),
      selectedId: signal(conv.id),
      selected: signal(conv),
      selectedMessages: signal(opts.messages ?? [buildMessage()]),
      load: jasmine.createSpy('load').and.resolveTo(undefined),
      select: jasmine.createSpy('select').and.resolveTo(undefined),
      pushIncoming: jasmine.createSpy('pushIncoming'),
      removeOnResolved: jasmine.createSpy('removeOnResolved'),
      send: jasmine.createSpy('send').and.resolveTo(undefined),
      resolve: jasmine.createSpy('resolve').and.resolveTo(undefined),
    };

    const wsStub = {
      connected: signal(false),
      waMessageStatuses: signal(opts.waStatuses ?? new Map()),
      waSessionWindows: signal(opts.waWindow ?? new Map()),
      connect: jasmine.createSpy('connect'),
      destroy: jasmine.createSpy('destroy'),
      resetSessionWindow: jasmine.createSpy('resetSessionWindow'),
    };

    const notifyStub = {
      requestPermission: jasmine.createSpy('requestPermission').and.resolveTo('granted'),
      notify: jasmine.createSpy('notify'),
    };

    TestBed.configureTestingModule({
      imports: [LiveChatInboxComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: InboxService, useValue: inboxStub },
        { provide: CrmWebSocketService, useValue: wsStub },
        { provide: BrowserNotificationService, useValue: notifyStub },
      ],
    });

    return { inboxStub, wsStub, notifyStub };
  }

  it('renderiza badge "WhatsApp" para conversa do canal whatsapp', async () => {
    setupHarness({ conv: buildConv({ channel: 'whatsapp' }) });
    const fixture: ComponentFixture<LiveChatInboxComponent> =
      TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.channel-badge.whatsapp');
    expect(badge).not.toBeNull();
    expect(badge!.textContent).toContain('WhatsApp');
  });

  it('renderiza badge "Web Chat" para conversa do canal live_chat', async () => {
    setupHarness({ conv: buildConv({ channel: 'live_chat' }) });
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.channel-badge.live-chat');
    expect(badge).not.toBeNull();
  });

  it('ícone de delivery vira ✓✓ em delivered', () => {
    const statuses = new Map<string, WaMessageStatusState>([
      ['msg-1', { status: 'delivered' as WaDeliveryStatus, timestamp: '2026-05-10T12:00:05Z' }],
    ]);
    setupHarness({ waStatuses: statuses });
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();

    const comp: any = fixture.componentInstance;
    const ico = comp.deliveryIcon('msg-1');
    expect(ico.glyph).toBe('✓✓');
    expect(ico.read).toBeFalse();
    expect(ico.failed).toBeFalse();
  });

  it('ícone vira ✓✓ azul (read=true) em status read', () => {
    const statuses = new Map<string, WaMessageStatusState>([
      ['msg-1', { status: 'read' as WaDeliveryStatus, timestamp: '2026-05-10T12:00:10Z' }],
    ]);
    setupHarness({ waStatuses: statuses });
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();

    const comp: any = fixture.componentInstance;
    const ico = comp.deliveryIcon('msg-1');
    expect(ico.read).toBeTrue();
    expect(ico.failed).toBeFalse();
  });

  it('ícone vira ✗ em failed com tooltip do erro', () => {
    const statuses = new Map<string, WaMessageStatusState>([
      ['msg-1', {
        status: 'failed' as WaDeliveryStatus,
        errorMessage: 'Recipient não autorizou',
        timestamp: '2026-05-10T12:00:11Z',
      }],
    ]);
    setupHarness({ waStatuses: statuses });
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();

    const comp: any = fixture.componentInstance;
    const ico = comp.deliveryIcon('msg-1');
    expect(ico.glyph).toBe('✗');
    expect(ico.failed).toBeTrue();
    expect(ico.tooltip).toBe('Recipient não autorizou');
  });

  it('mensagem sem status conhecido fallback para ✓ "aguardando confirmação"', () => {
    setupHarness();
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();

    const comp: any = fixture.componentInstance;
    const ico = comp.deliveryIcon('msg-1');
    expect(ico.glyph).toBe('✓');
    expect(ico.tooltip).toContain('aguardando');
  });

  it('banner janela 24h é null quando não-WhatsApp', () => {
    setupHarness({ conv: buildConv({ channel: 'live_chat' }) });
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();

    const comp: any = fixture.componentInstance;
    expect(comp.sessionWindowBanner()).toBeNull();
  });

  it('banner janela 24h kind=warn quando expiring', () => {
    const w = new Map<string, WaSessionWindowState>([
      ['conv-1', { status: 'expiring', expiresAt: '2026-05-10T13:00:00Z', minutesRemaining: 47 }],
    ]);
    setupHarness({ waWindow: w });
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();

    const comp: any = fixture.componentInstance;
    const banner = comp.sessionWindowBanner();
    expect(banner?.kind).toBe('warn');
    expect(banner?.text).toContain('47 min');
  });

  it('banner janela 24h kind=danger quando expired', () => {
    const w = new Map<string, WaSessionWindowState>([
      ['conv-1', { status: 'expired', expiredAt: '2026-05-09T12:00:00Z' }],
    ]);
    setupHarness({ waWindow: w });
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    fixture.detectChanges();

    const comp: any = fixture.componentInstance;
    const banner = comp.sessionWindowBanner();
    expect(banner?.kind).toBe('danger');
    expect(banner?.text).toContain('expirou');
  });

  it('isAudioAttachment detecta extensões de áudio', () => {
    setupHarness();
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    const comp: any = fixture.componentInstance;
    expect(comp.isAudioAttachment('voice.ogg')).toBeTrue();
    expect(comp.isAudioAttachment('clip.mp3')).toBeTrue();
    expect(comp.isAudioAttachment('audio.m4a')).toBeTrue();
    expect(comp.isAudioAttachment('photo.jpg')).toBeFalse();
    expect(comp.isAudioAttachment(null)).toBeFalse();
  });

  it('isAttachmentFailed detecta marker _failed:', () => {
    setupHarness();
    const fixture = TestBed.createComponent(LiveChatInboxComponent);
    const comp: any = fixture.componentInstance;
    expect(comp.isAttachmentFailed('_failed:download_failed')).toBeTrue();
    expect(comp.isAttachmentFailed('image.jpg')).toBeFalse();
    expect(comp.isAttachmentFailed(null)).toBeFalse();
  });
});
