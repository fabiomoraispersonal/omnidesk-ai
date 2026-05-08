import { Component, Input, OnInit, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SelectButtonModule } from 'primeng/selectbutton';
import { MessageService } from 'primeng/api';
import { ToastModule } from 'primeng/toast';
import { PresenceSignal, AttendanceStatus } from '../../../core/presence/presence.signal';
import { PresenceService } from '../../../core/presence/presence.service';

interface StatusOption { label: string; value: AttendanceStatus; }

@Component({
  selector: 'omni-attendant-status-toggle',
  standalone: true,
  imports: [CommonModule, FormsModule, SelectButtonModule, ToastModule],
  providers: [MessageService],
  templateUrl: './attendant-status-toggle.component.html',
})
export class AttendantStatusToggleComponent implements OnInit {
  private readonly presenceService = inject(PresenceService);
  private readonly presenceSignal = inject(PresenceSignal);
  private readonly toast = inject(MessageService);

  @Input({ required: true }) attendantId!: string;

  protected readonly options: StatusOption[] = [
    { label: 'Online', value: 'online' },
    { label: 'Ausente', value: 'away' },
    { label: 'Offline', value: 'offline' },
  ];

  protected readonly current = computed(() => this.presenceSignal.current().status);

  ngOnInit(): void {
    this.presenceService.start(this.attendantId);
  }

  onChange(value: AttendanceStatus): void {
    this.presenceService.setStatus(this.attendantId, value).subscribe({
      error: err => this.toast.add({
        severity: 'error',
        summary: 'Falha ao alterar status',
        detail: err?.error?.error?.message ?? 'Tente novamente em instantes.',
      }),
    });
  }
}
