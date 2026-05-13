import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WeeklyGridComponent } from './weekly-grid.component';
import { AppointmentsService } from './appointments.service';
import { ProfessionalsService } from '../professionals/professionals.service';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('WeeklyGridComponent', () => {
  let comp: WeeklyGridComponent;
  let fixture: ComponentFixture<WeeklyGridComponent>;
  let apptSvc: jasmine.SpyObj<AppointmentsService>;
  let profSvc: jasmine.SpyObj<ProfessionalsService>;

  beforeEach(async () => {
    apptSvc = jasmine.createSpyObj('AppointmentsService', ['list']);
    profSvc = jasmine.createSpyObj('ProfessionalsService', ['list']);
    apptSvc.list.and.resolveTo({ items: [], total: 0 });
    profSvc.list.and.resolveTo({ items: [], total: 0 });

    await TestBed.configureTestingModule({
      imports: [WeeklyGridComponent, RouterTestingModule, HttpClientTestingModule],
      providers: [
        { provide: AppointmentsService, useValue: apptSvc },
        { provide: ProfessionalsService, useValue: profSvc },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WeeklyGridComponent);
    comp    = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('renders 7 day columns', () => {
    expect(comp.columns().length).toBe(7);
  });

  it('columns start on Monday', () => {
    expect(comp.columns()[0].dayOfWeek).toBe(1);
  });
});
