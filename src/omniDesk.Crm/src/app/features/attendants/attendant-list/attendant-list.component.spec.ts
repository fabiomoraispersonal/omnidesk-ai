import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AttendantListComponent } from './attendant-list.component';
import { AttendantService } from '../services/attendant.service';

describe('AttendantListComponent', () => {
  const service = {
    list: jasmine.createSpy('list').and.returnValue(of([
      { id: 'a1', name: 'Maria', departmentIds: ['d1'], status: 'online',
        activeTicketCount: 1, maxSimultaneousChats: 5, avatarUrl: null, isActive: true },
    ])),
    deactivate: jasmine.createSpy('deactivate').and.returnValue(of(void 0)),
  };
  let fixture: ComponentFixture<AttendantListComponent>;

  beforeEach(async () => {
    service.list.calls.reset();
    await TestBed.configureTestingModule({
      imports: [AttendantListComponent],
      providers: [
        provideRouter([]),
        { provide: AttendantService, useValue: service },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AttendantListComponent);
    fixture.detectChanges();
  });

  it('loads and renders attendants', () => {
    expect(service.list).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Maria');
  });

  it('maps online status to success severity', () => {
    const cmp = fixture.componentInstance as any;
    expect(cmp.statusSeverity('online')).toBe('success');
    expect(cmp.statusSeverity('away')).toBe('warning');
    expect(cmp.statusSeverity('offline')).toBe('secondary');
  });
});
