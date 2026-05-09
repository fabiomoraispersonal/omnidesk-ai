import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DepartmentListComponent } from './department-list.component';
import { DepartmentService } from '../services/department.service';

describe('DepartmentListComponent', () => {
  const service = {
    list: jasmine.createSpy('list').and.returnValue(of([
      { id: 'd1', name: 'Comercial', isActive: true, attendantCount: 3, activeTicketCount: 2,
        businessHours: { start: '08:00', end: '18:00', days: [1,2,3,4,5] } },
    ])),
    deactivate: jasmine.createSpy('deactivate').and.returnValue(of(void 0)),
  };
  let fixture: ComponentFixture<DepartmentListComponent>;

  beforeEach(async () => {
    service.list.calls.reset();
    await TestBed.configureTestingModule({
      imports: [DepartmentListComponent],
      providers: [
        provideRouter([]),
        { provide: DepartmentService, useValue: service },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DepartmentListComponent);
    fixture.detectChanges();
  });

  it('loads and renders departments', () => {
    expect(service.list).toHaveBeenCalledWith(false);
    expect(fixture.nativeElement.textContent).toContain('Comercial');
  });
});
