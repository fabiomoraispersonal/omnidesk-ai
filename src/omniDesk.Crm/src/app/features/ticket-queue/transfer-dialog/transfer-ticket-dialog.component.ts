import { Component, EventEmitter, Input, Output, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { catchError, of } from 'rxjs';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { Department, DepartmentService, DepartmentAttendantSummary } from '../../departments/services/department.service';
import { environment } from '../../../../environments/environment';

export interface TransferRequest {
  toAttendantId: string | null;
  toDepartmentId: string | null;
  reason: string | null;
}

@Component({
  selector: 'omni-transfer-ticket-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, ButtonModule, InputTextModule, SelectModule],
  templateUrl: './transfer-ticket-dialog.component.html',
})
export class TransferTicketDialogComponent implements OnInit {
  private readonly departmentService = inject(DepartmentService);
  private readonly http = inject(HttpClient);

  @Input({ required: true }) ticketId!: string;
  @Input() visible = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() transferred = new EventEmitter<TransferRequest>();

  protected readonly departments = signal<Department[]>([]);
  protected readonly attendantOptions = signal<DepartmentAttendantSummary[]>([]);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly submitting = signal(false);

  protected mode: 'attendant' | 'department' = 'attendant';
  protected selectedDepartmentId: string | null = null;
  protected selectedAttendantId: string | null = null;
  protected reason = '';

  ngOnInit(): void {
    this.departmentService.list(false).subscribe(d => this.departments.set(d));
  }

  onDepartmentChange(deptId: string | null): void {
    this.selectedDepartmentId = deptId;
    this.selectedAttendantId = null;
    if (deptId && this.mode === 'attendant') {
      this.departmentService.attendants(deptId).subscribe(a => this.attendantOptions.set(a));
    } else {
      this.attendantOptions.set([]);
    }
  }

  submit(): void {
    if (!this.canSubmit()) return;
    const payload = {
      toAttendantId: this.mode === 'attendant' ? this.selectedAttendantId : null,
      toDepartmentId: this.mode === 'department' ? this.selectedDepartmentId : null,
      reason: this.reason?.trim() || null,
    };
    this.submitting.set(true);
    this.errorMessage.set(null);
    this.http.post<{ data: { outcome: string } }>(
      `${environment.apiUrl}/api/tickets/${this.ticketId}/transfer`,
      payload,
    ).pipe(catchError(err => {
      this.errorMessage.set(err?.error?.error?.message ?? 'Falha ao transferir.');
      this.submitting.set(false);
      return of(null);
    })).subscribe(resp => {
      this.submitting.set(false);
      if (!resp) return;
      this.transferred.emit(payload);
      this.close();
    });
  }

  canSubmit(): boolean {
    if (this.mode === 'attendant') return !!this.selectedAttendantId;
    return !!this.selectedDepartmentId;
  }

  close(): void {
    this.visible = false;
    this.visibleChange.emit(false);
  }
}
