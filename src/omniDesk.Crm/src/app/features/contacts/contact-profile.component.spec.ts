import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute } from '@angular/router';
import { ContactProfileComponent } from './contact-profile.component';
import { ContactsService } from './services/contacts.service';

// ---------------------------------------------------------------------------
// Spec 009 US6 — T158
// Smoke tests for ContactProfileComponent.
// ---------------------------------------------------------------------------

describe('ContactProfileComponent', () => {
  let fixture: ComponentFixture<ContactProfileComponent>;
  let component: ContactProfileComponent;
  let contactsService: jasmine.SpyObj<ContactsService>;

  const mockContact = {
    id: 'c-1',
    name: 'João Silva',
    email: 'joao@email.com',
    phone: '+5511999999999',
    notes: null,
    source_channels: ['manual'],
    tickets_count: 5,
    conversations_count: 2,
    last_interaction_at: null,
    created_at: '2026-01-01T00:00:00Z',
    updated_at: '2026-01-01T00:00:00Z',
  };

  beforeEach(async () => {
    contactsService = jasmine.createSpyObj('ContactsService', ['get'], {
      loading: { set: () => {}, (): boolean { return false; } },
      contact: { set: () => {}, (): null { return null; } },
    });
    contactsService.get.and.returnValue(Promise.resolve(mockContact));

    await TestBed.configureTestingModule({
      imports: [ContactProfileComponent, NoopAnimationsModule],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: ContactsService, useValue: contactsService },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'c-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ContactProfileComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('reads contactId from route params', () => {
    expect(component.contactId()).toBe('c-1');
  });

  it('calls ContactsService.get with correct id', () => {
    expect(contactsService.get).toHaveBeenCalledWith('c-1');
  });

  it('shows loading text initially', () => {
    component.loading.set(true);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Carregando');
  });

  it('shows not-found text when contact is null', () => {
    component.loading.set(false);
    component.contact.set(null);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('não encontrado');
  });

  it('shows contact name when loaded', async () => {
    component.loading.set(false);
    component.contact.set(mockContact);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('João Silva');
  });
});
