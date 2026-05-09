import { ComponentFixture, TestBed } from '@angular/core/testing';
import { computed, signal } from '@angular/core';
import { of } from 'rxjs';
import { QueueBoardComponent } from './queue-board.component';
import { DepartmentService } from '../../departments/services/department.service';
import { TicketQueueService } from '../services/ticket-queue.service';

describe('QueueBoardComponent', () => {
  const queues = signal<Record<string, any[]>>({});
  const queueService = {
    start: jasmine.createSpy('start'),
    queues: computed(() => queues()),
  };
  const departmentService = {
    list: jasmine.createSpy('list').and.returnValue(of([
      { id: 'd1', name: 'Comercial', isActive: true },
    ])),
  };
  let fixture: ComponentFixture<QueueBoardComponent>;

  beforeEach(async () => {
    queueService.start.calls.reset();
    queues.set({});
    await TestBed.configureTestingModule({
      imports: [QueueBoardComponent],
      providers: [
        { provide: DepartmentService, useValue: departmentService },
        { provide: TicketQueueService, useValue: queueService },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(QueueBoardComponent);
    fixture.detectChanges();
  });

  it('starts queue subscription with active department ids', () => {
    expect(queueService.start).toHaveBeenCalledWith(['d1']);
  });

  it('renders the empty state when no tickets queued', () => {
    expect(fixture.nativeElement.textContent).toContain('Nenhum ticket na fila');
  });

  it('maps reason to localized label', () => {
    const cmp = fixture.componentInstance as any;
    expect(cmp.reasonLabel('AllAtCapacity')).toContain('limite');
    expect(cmp.reasonSeverity('OutsideBusinessHoursNoOneOnline')).toBe('danger');
  });
});
