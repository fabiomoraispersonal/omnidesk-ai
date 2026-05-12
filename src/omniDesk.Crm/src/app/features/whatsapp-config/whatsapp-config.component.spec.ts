import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { WhatsAppConfigComponent } from './whatsapp-config.component';
import { WhatsAppConfigService } from './services/whatsapp-config.service';
import { WhatsAppConfig } from './services/whatsapp-config.types';
import { RoleSignal, ROLES, Role } from '../../core/authorization/role.signal';

/**
 * Spec 008 T075 — testes do orquestrador da tela WhatsApp Config.
 * Cobre RBAC visível (tenant_admin vs supervisor) + carregamento inicial.
 */
describe('WhatsAppConfigComponent', () => {
  const baseConfig: WhatsAppConfig = {
    is_enabled: false,
    phone_number: '+5511999999999',
    display_name: 'Clínica ABC',
    waba_id: 'WABA_123',
    phone_number_id: '123456789',
    access_token_configured: true,
    app_secret_configured: true,
    webhook_verify_token: 'verify_xyz',
    webhook_url: 'https://api.test/api/public/whatsapp/webhook/clinica-abc',
    business_hours_enabled: false,
    channel_status: 'configured_inactive',
    updated_at: '2026-05-10T10:00:00Z',
  };

  function buildHarness(role: Role | null) {
    const serviceStub = {
      config: signal<WhatsAppConfig | null>(baseConfig),
      channelStatus: signal(baseConfig.channel_status),
      isEnabled: signal(baseConfig.is_enabled),
      loading: signal(false),
      saving: signal(false),
      load: jasmine.createSpy('load').and.resolveTo(undefined),
      save: jasmine.createSpy('save').and.resolveTo(baseConfig),
      toggle: jasmine.createSpy('toggle').and.resolveTo({
        is_enabled: true,
        channel_status: 'active',
      }),
    };

    const roleSignalStub = { role: signal<Role | null>(role) };

    TestBed.configureTestingModule({
      imports: [WhatsAppConfigComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: WhatsAppConfigService, useValue: serviceStub },
        { provide: RoleSignal, useValue: roleSignalStub },
      ],
    });

    return { serviceStub, roleSignalStub };
  }

  it('chama service.load() no init', async () => {
    const { serviceStub } = buildHarness(ROLES.TenantAdmin);
    const fixture: ComponentFixture<WhatsAppConfigComponent> = TestBed.createComponent(WhatsAppConfigComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(serviceStub.load).toHaveBeenCalledTimes(1);
  });

  it('renderiza botão de toggle para tenant_admin', async () => {
    buildHarness(ROLES.TenantAdmin);
    const fixture = TestBed.createComponent(WhatsAppConfigComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const toggleBtn: HTMLElement | null = fixture.nativeElement.querySelector('.page-header button');
    expect(toggleBtn).not.toBeNull();
  });

  it('esconde botão de toggle para supervisor (readOnly)', async () => {
    buildHarness(ROLES.Supervisor);
    const fixture = TestBed.createComponent(WhatsAppConfigComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const toggleBtn: HTMLElement | null = fixture.nativeElement.querySelector('.page-header button');
    expect(toggleBtn).toBeNull();
  });

  it('renderiza badge de status do canal', async () => {
    buildHarness(ROLES.TenantAdmin);
    const fixture = TestBed.createComponent(WhatsAppConfigComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const badge: HTMLElement | null = fixture.nativeElement.querySelector('app-channel-status-badge');
    expect(badge).not.toBeNull();
  });
});
