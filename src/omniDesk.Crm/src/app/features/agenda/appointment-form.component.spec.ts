import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppointmentFormComponent } from './appointment-form.component';
import { AppointmentsService } from './appointments.service';
import { AvailabilityService } from './availability.service';
import { ServicesCatalogService } from '../services-catalog/services.service';
import { ProfessionalsService } from '../professionals/professionals.service';
import { RouterTestingModule } from '@angular/router/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('AppointmentFormComponent', () => {
  let comp: AppointmentFormComponent;
  let fixture: ComponentFixture<AppointmentFormComponent>;

  beforeEach(async () => {
    const apptSvc  = jasmine.createSpyObj('AppointmentsService', ['create']);
    const availSvc = jasmine.createSpyObj('AvailabilityService', ['getSlots']);
    const svcSvc   = jasmine.createSpyObj('ServicesCatalogService', ['list']);
    const profSvc  = jasmine.createSpyObj('ProfessionalsService', ['list', 'getServices']);
    profSvc.list.and.resolveTo({ items: [], total: 0 });

    await TestBed.configureTestingModule({
      imports: [AppointmentFormComponent, RouterTestingModule, NoopAnimationsModule, HttpClientTestingModule],
      providers: [
        { provide: AppointmentsService,     useValue: apptSvc },
        { provide: AvailabilityService,     useValue: availSvc },
        { provide: ServicesCatalogService,  useValue: svcSvc },
        { provide: ProfessionalsService,    useValue: profSvc },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AppointmentFormComponent);
    comp    = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('form is invalid when required fields are empty', () => {
    expect(comp.form.invalid).toBeTrue();
  });

  it('slotOptions returns formatted time labels', () => {
    comp['slots'].set([{ start_at: '2026-06-10T12:00:00Z', end_at: '2026-06-10T12:30:00Z' }]);
    const opts = comp.slotOptions();
    expect(opts.length).toBe(1);
    expect(opts[0].value).toBe('2026-06-10T12:00:00Z');
  });
});
