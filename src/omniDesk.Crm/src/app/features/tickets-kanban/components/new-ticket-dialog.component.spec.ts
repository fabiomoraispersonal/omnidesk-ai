import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { NewTicketDialogComponent } from './new-ticket-dialog.component';

// ---------------------------------------------------------------------------
// Spec 009 US5 — T140
// Smoke tests for NewTicketDialogComponent.
// ---------------------------------------------------------------------------

describe('NewTicketDialogComponent', () => {
  let fixture: ComponentFixture<NewTicketDialogComponent>;
  let component: NewTicketDialogComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NewTicketDialogComponent, NoopAnimationsModule],
      providers: [provideHttpClient()],
    }).compileComponents();

    fixture = TestBed.createComponent(NewTicketDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('is hidden by default', () => {
    expect(component.visible()).toBeFalse();
  });

  it('opens when open() is called', () => {
    spyOn<any>(component, 'loadDepts').and.stub();
    component.open();
    expect(component.visible()).toBeTrue();
  });

  it('canSubmit is false when no department selected', () => {
    component.selectedDeptId.set(null);
    component.subject.set('Test subject');
    component.contactName.set('João');
    expect(component.canSubmit()).toBeFalse();
  });

  it('canSubmit is false when subject is too short', () => {
    component.selectedDeptId.set('dept-1');
    component.subject.set('ab');
    component.contactName.set('João');
    expect(component.canSubmit()).toBeFalse();
  });

  it('canSubmit is false when no contact information provided', () => {
    component.selectedDeptId.set('dept-1');
    component.subject.set('Valid subject');
    component.selectedContactId.set(null);
    component.contactName.set('');
    component.contactEmail.set('');
    component.contactPhone.set('');
    expect(component.canSubmit()).toBeFalse();
  });

  it('canSubmit is true when dept, subject ≥ 3 chars and existing contact selected', () => {
    component.selectedDeptId.set('dept-1');
    component.subject.set('Valid subject');
    component.selectedContactId.set('contact-123');
    expect(component.canSubmit()).toBeTrue();
  });

  it('canSubmit is true when dept, subject ≥ 3 chars and contact name provided', () => {
    component.selectedDeptId.set('dept-1');
    component.subject.set('Valid subject');
    component.selectedContactId.set(null);
    component.contactName.set('João Silva');
    expect(component.canSubmit()).toBeTrue();
  });

  it('canSubmit is true when dept, subject ≥ 3 chars and contact email provided', () => {
    component.selectedDeptId.set('dept-1');
    component.subject.set('Valid subject');
    component.selectedContactId.set(null);
    component.contactEmail.set('joao@email.com');
    expect(component.canSubmit()).toBeTrue();
  });

  it('emits cancelled on cancel', () => {
    let emitted = false;
    component.cancelled.subscribe(() => (emitted = true));
    component.onCancel();
    expect(emitted).toBeTrue();
    expect(component.visible()).toBeFalse();
  });

  it('onContactSelect sets selectedContactId', () => {
    const contact = { id: 'c-1', name: 'Maria', email: 'maria@email.com' };
    component.onContactSelect(contact);
    expect(component.selectedContactId()).toBe('c-1');
  });

  it('onContactClear clears selectedContactId', () => {
    component.selectedContactId.set('c-1');
    component.onContactClear();
    expect(component.selectedContactId()).toBeNull();
    expect(component.selectedContact).toBeNull();
  });

  it('new-contact fields are shown when no existing contact is selected', () => {
    component.selectedContactId.set(null);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const newContactGroup = el.querySelector('.new-contact');
    expect(newContactGroup).toBeTruthy();
  });

  it('new-contact fields are hidden when an existing contact is selected', () => {
    component.selectedContactId.set('c-existing');
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const newContactGroup = el.querySelector('.new-contact');
    expect(newContactGroup).toBeNull();
  });
});
