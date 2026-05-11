import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ConfirmationService } from 'primeng/api';
import { WhatsAppTemplatesComponent } from './whatsapp-templates.component';
import { WhatsAppTemplatesService } from './services/whatsapp-templates.service';
import { WhatsAppTemplate } from './services/whatsapp-templates.types';
import { ROLES, RoleSignal, Role } from '../../core/authorization/role.signal';

describe('WhatsAppTemplatesComponent', () => {
  const sample: WhatsAppTemplate = {
    id: 'tpl-1',
    type: 'appointment_reminder',
    name: 'lembrete_consulta_clinicaabc',
    category: 'utility',
    language: 'pt_BR',
    status: 'draft',
    body_template: 'Olá, {{1}}! Consulta em {{2}} às {{3}}.',
    variable_labels: ['nome', 'data', 'hora'],
    variable_count: 3,
    rejection_reason: null,
    submitted_at: null,
    approved_at: null,
    rejected_at: null,
    meta_template_id: null,
    created_at: '2026-05-10T10:00:00Z',
    updated_at: '2026-05-10T10:00:00Z',
  };

  function buildHarness(role: Role | null) {
    const serviceStub = {
      templates: signal<WhatsAppTemplate[]>([sample]),
      total: signal(1),
      page: signal(1),
      perPage: signal(20),
      loading: signal(false),
      saving: signal(false),
      list: jasmine.createSpy('list').and.resolveTo({
        items: [sample], total: 1, page: 1, per_page: 20,
      }),
      create: jasmine.createSpy('create').and.resolveTo(sample),
      update: jasmine.createSpy('update').and.resolveTo(sample),
      submit: jasmine.createSpy('submit').and.resolveTo({ ...sample, status: 'pending_meta' }),
      delete: jasmine.createSpy('delete').and.resolveTo(undefined),
    };

    const roleSignalStub = { role: signal<Role | null>(role) };

    TestBed.configureTestingModule({
      imports: [WhatsAppTemplatesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: WhatsAppTemplatesService, useValue: serviceStub },
        { provide: RoleSignal, useValue: roleSignalStub },
        ConfirmationService,
      ],
    });

    return { serviceStub };
  }

  it('chama service.list({}) no init', async () => {
    const { serviceStub } = buildHarness(ROLES.TenantAdmin);
    const fixture: ComponentFixture<WhatsAppTemplatesComponent> =
      TestBed.createComponent(WhatsAppTemplatesComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(serviceStub.list).toHaveBeenCalledWith({});
  });

  it('renderiza botão "Novo template" para tenant_admin', async () => {
    buildHarness(ROLES.TenantAdmin);
    const fixture = TestBed.createComponent(WhatsAppTemplatesComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const btn: HTMLElement | null = fixture.nativeElement.querySelector('.page-header button');
    expect(btn).not.toBeNull();
    expect(btn!.textContent).toContain('Novo template');
  });

  it('esconde "Novo template" para attendant (readOnly)', async () => {
    buildHarness(ROLES.Attendant);
    const fixture = TestBed.createComponent(WhatsAppTemplatesComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const btn: HTMLElement | null = fixture.nativeElement.querySelector('.page-header button');
    expect(btn).toBeNull();
  });

  it('exibe lista quando templates() não está vazia', async () => {
    buildHarness(ROLES.Supervisor);
    const fixture = TestBed.createComponent(WhatsAppTemplatesComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const list = fixture.nativeElement.querySelector('app-template-list');
    expect(list).not.toBeNull();
  });

  it('openCreate seta editor para modo create + visível', () => {
    buildHarness(ROLES.TenantAdmin);
    const fixture = TestBed.createComponent(WhatsAppTemplatesComponent);
    const comp: any = fixture.componentInstance;

    comp.openCreate();
    expect(comp.editorVisible()).toBeTrue();
    expect(comp.editorMode()).toBe('create');
    expect(comp.editingTemplate()).toBeNull();
  });

  it('openEdit seta modo edit + template selecionado', () => {
    buildHarness(ROLES.TenantAdmin);
    const fixture = TestBed.createComponent(WhatsAppTemplatesComponent);
    const comp: any = fixture.componentInstance;

    comp.openEdit(sample);
    expect(comp.editorVisible()).toBeTrue();
    expect(comp.editorMode()).toBe('edit');
    expect(comp.editingTemplate()).toEqual(sample);
  });

  it('closeEditor fecha + limpa template', () => {
    buildHarness(ROLES.TenantAdmin);
    const fixture = TestBed.createComponent(WhatsAppTemplatesComponent);
    const comp: any = fixture.componentInstance;

    comp.openEdit(sample);
    comp.closeEditor();
    expect(comp.editorVisible()).toBeFalse();
    expect(comp.editingTemplate()).toBeNull();
  });
});
