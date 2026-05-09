import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardModule } from 'primeng/card';
import { BadgeModule } from 'primeng/badge';
import { TagModule } from 'primeng/tag';
import { Department, DepartmentService } from '../../departments/services/department.service';
import { TicketQueueService } from '../services/ticket-queue.service';

@Component({
  selector: 'omni-queue-board',
  standalone: true,
  imports: [CommonModule, CardModule, BadgeModule, TagModule],
  templateUrl: './queue-board.component.html',
})
export class QueueBoardComponent implements OnInit {
  private readonly departmentsApi = inject(DepartmentService);
  private readonly queueService = inject(TicketQueueService);

  protected readonly departments = signal<Department[]>([]);
  protected readonly queues = this.queueService.queues;

  protected readonly columns = computed(() =>
    this.departments().map(d => ({
      department: d,
      queue: this.queues()[d.id] ?? [],
    })),
  );

  ngOnInit(): void {
    this.departmentsApi.list(false).subscribe(list => {
      this.departments.set(list);
      this.queueService.start(list.map(d => d.id));
    });
  }

  reasonLabel(reason: string): string {
    switch (reason) {
      case 'NoAttendantsOnline': return 'Sem atendentes online';
      case 'AllAtCapacity': return 'Todos no limite';
      case 'OutsideBusinessHoursNoOneOnline': return 'Fora do expediente';
      default: return reason;
    }
  }

  reasonSeverity(reason: string): 'info' | 'warning' | 'danger' {
    if (reason === 'OutsideBusinessHoursNoOneOnline') return 'danger';
    if (reason === 'AllAtCapacity') return 'warning';
    return 'info';
  }
}
