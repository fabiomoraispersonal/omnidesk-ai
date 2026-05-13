import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppointmentDetailComponent } from './appointment-detail.component';
import { AppointmentsService } from './appointments.service';
import { ActivatedRoute } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

describe('AppointmentDetailComponent', () => {
  let comp: AppointmentDetailComponent;
  let fixture: ComponentFixture<AppointmentDetailComponent>;
  let svc: jasmine.SpyObj<AppointmentsService>;

  beforeEach(async () => {
    svc = jasmine.createSpyObj('AppointmentsService', ['get', 'confirm', 'cancel', 'noShow', 'resendReminder']);
    svc.get.and.resolveTo({
      id: 'appt-1', status: 'pending_confirmation', professional: null, service: null,
      professional_id: 'p1', service_id: 's1',
      contact_id: null, ticket_id: null, conversation_id: null,
      start_at: '2026-06-10T12:00:00Z', end_at: '2026-06-10T12:30:00Z',
      client_type: 'new_client', created_by: 'attendant', notes: null,
      reminder_sent_at: null, cancelled_by: null, cancelled_at: null,
      cancellation_reason: null, created_at: '', updated_at: '',
    } as any);

    await TestBed.configureTestingModule({
      imports: [AppointmentDetailComponent, RouterTestingModule, NoopAnimationsModule],
      providers: [
        { provide: AppointmentsService, useValue: svc },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'appt-1' } } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AppointmentDetailComponent);
    comp    = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('loads appointment on init', () => {
    expect(svc.get).toHaveBeenCalledWith('appt-1');
    expect(comp.appt()?.id).toBe('appt-1');
  });

  it('isPending is true for pending_confirmation', () => {
    expect(comp.isPending).toBeTrue();
    expect(comp.isTerminal).toBeFalse();
  });

  it('statusLabel returns readable label', () => {
    expect(comp.statusLabel('confirmed')).toBe('Confirmado');
    expect(comp.statusLabel('cancelled')).toBe('Cancelado');
  });
});
