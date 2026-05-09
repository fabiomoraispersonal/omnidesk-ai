import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { TransferTicketDialogComponent } from './transfer-ticket-dialog.component';
import { DepartmentService } from '../../departments/services/department.service';
import { environment } from '../../../../environments/environment';

describe('TransferTicketDialogComponent', () => {
  const departmentService = {
    list: jasmine.createSpy('list').and.returnValue(of([{ id: 'd1', name: 'Comercial' }])),
    attendants: jasmine.createSpy('attendants').and.returnValue(of([
      { attendantId: 'a1', name: 'Maria', avatarUrl: null, activeTicketCount: 0, maxSimultaneousChats: 5, isPrimaryDepartment: true, status: 'online' },
    ])),
  };
  let fixture: ComponentFixture<TransferTicketDialogComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    departmentService.list.calls.reset();
    departmentService.attendants.calls.reset();
    await TestBed.configureTestingModule({
      imports: [TransferTicketDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DepartmentService, useValue: departmentService },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TransferTicketDialogComponent);
    fixture.componentInstance.ticketId = 't1';
    fixture.detectChanges();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('disables submit until target is selected', () => {
    const cmp = fixture.componentInstance as any;
    expect(cmp.canSubmit()).toBeFalse();
  });

  it('loads attendants when a department is selected in attendant mode', () => {
    const cmp = fixture.componentInstance as any;
    cmp.mode = 'attendant';
    cmp.onDepartmentChange('d1');
    expect(departmentService.attendants).toHaveBeenCalledWith('d1');
  });

  it('POSTs the canonical payload on submit', () => {
    const cmp = fixture.componentInstance as any;
    cmp.mode = 'attendant';
    cmp.selectedAttendantId = 'a1';
    cmp.selectedDepartmentId = 'd1';
    cmp.reason = 'cliente quer suporte';
    cmp.submit();

    const req = http.expectOne(`${environment.apiUrl}/api/tickets/t1/transfer`);
    expect(req.request.body).toEqual({
      toAttendantId: 'a1',
      toDepartmentId: null,
      reason: 'cliente quer suporte',
    });
    req.flush({ data: { outcome: 'TransferredToAttendant' } });
  });

  it('switches payload shape when transferring to a department only', () => {
    const cmp = fixture.componentInstance as any;
    cmp.mode = 'department';
    cmp.selectedDepartmentId = 'd2';
    cmp.submit();
    const req = http.expectOne(`${environment.apiUrl}/api/tickets/t1/transfer`);
    expect(req.request.body.toAttendantId).toBeNull();
    expect(req.request.body.toDepartmentId).toBe('d2');
    req.flush({ data: { outcome: 'TransferredToDepartmentQueue' } });
  });
});
