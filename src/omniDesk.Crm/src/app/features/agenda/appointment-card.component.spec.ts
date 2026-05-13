import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppointmentCardComponent } from './appointment-card.component';
import { AppointmentDto } from './appointments.service';

const mockAppt = (overrides: Partial<AppointmentDto> = {}): AppointmentDto => ({
  id: '1', professional_id: 'p1', service_id: 's1',
  contact_id: null, ticket_id: null, conversation_id: null,
  start_at: '2026-06-10T12:00:00Z', end_at: '2026-06-10T12:30:00Z',
  status: 'confirmed', client_type: 'new_client', created_by: 'attendant',
  notes: null, reminder_sent_at: null, cancelled_by: null, cancelled_at: null,
  cancellation_reason: null, created_at: '2026-06-01T00:00:00Z', updated_at: '2026-06-01T00:00:00Z',
  professional: { id: 'p1', name: 'Dr. Teste' },
  service: { id: 's1', name: 'Consulta', duration_minutes: 30, price: null },
  ...overrides,
});

describe('AppointmentCardComponent', () => {
  let fixture: ComponentFixture<AppointmentCardComponent>;
  let comp: AppointmentCardComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppointmentCardComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(AppointmentCardComponent);
    comp    = fixture.componentInstance;
    comp.appointment = mockAppt();
    fixture.detectChanges();
  });

  it('statusSeverity is success for confirmed', () => {
    expect(comp.statusSeverity).toBe('success');
  });

  it('statusSeverity is warning for pending_confirmation', () => {
    comp.appointment = mockAppt({ status: 'pending_confirmation' });
    expect(comp.statusSeverity).toBe('warning');
  });

  it('clientLabel is Novo for new_client', () => {
    expect(comp.clientLabel).toBe('Novo');
  });

  it('emits clicked event on click', () => {
    const spy = jasmine.createSpy();
    comp.clicked.subscribe(spy);
    fixture.nativeElement.querySelector('.appt-card').click();
    expect(spy).toHaveBeenCalledWith(comp.appointment);
  });
});
