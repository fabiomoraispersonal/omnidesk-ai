import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { WeeklyScheduleComponent } from './weekly-schedule.component';
import { ProfessionalsService } from './professionals.service';

describe('WeeklyScheduleComponent', () => {
  const stubSvc = {
    getSchedule: () => Promise.resolve([
      { id: 'w1', professional_id: 'p1', day_of_week: 1, start_time: '08:00', end_time: '17:00' },
    ]),
    updateSchedule: () => Promise.resolve([]),
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WeeklyScheduleComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ProfessionalsService, useValue: stubSvc },
      ],
    }).compileComponents();
  });

  it('loads existing slots', async () => {
    const fixture = TestBed.createComponent(WeeklyScheduleComponent);
    fixture.componentInstance.professionalId = 'p1';
    await fixture.whenStable();
    expect(fixture.componentInstance.slotsArray.length).toBe(1);
    expect(fixture.componentInstance.slotsArray.at(0).value.day_of_week).toBe(1);
  });

  it('addSlot increases count', async () => {
    const fixture = TestBed.createComponent(WeeklyScheduleComponent);
    fixture.componentInstance.professionalId = 'p1';
    await fixture.whenStable();
    fixture.componentInstance.addSlot();
    expect(fixture.componentInstance.slotsArray.length).toBe(2);
  });

  it('removeSlot decreases count', async () => {
    const fixture = TestBed.createComponent(WeeklyScheduleComponent);
    fixture.componentInstance.professionalId = 'p1';
    await fixture.whenStable();
    fixture.componentInstance.removeSlot(0);
    expect(fixture.componentInstance.slotsArray.length).toBe(0);
  });
});
