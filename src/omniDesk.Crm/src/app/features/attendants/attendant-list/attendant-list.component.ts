import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { AvatarModule } from 'primeng/avatar';
import { Attendant, AttendantService } from '../services/attendant.service';

@Component({
  selector: 'omni-attendant-list',
  standalone: true,
  imports: [CommonModule, TableModule, ButtonModule, TagModule, AvatarModule],
  templateUrl: './attendant-list.component.html',
})
export class AttendantListComponent implements OnInit {
  private readonly service = inject(AttendantService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly attendants = signal<Attendant[]>([]);

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.loading.set(true);
    this.service.list().subscribe({
      next: list => { this.attendants.set(list); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  newAttendant(): void { this.router.navigate(['/attendants/new']); }
  edit(id: string): void { this.router.navigate(['/attendants', id, 'edit']); }
  deactivate(id: string): void {
    if (!confirm('Desativar atendente?')) return;
    this.service.deactivate(id).subscribe(() => this.refresh());
  }

  statusSeverity(s: string | null): 'success' | 'warning' | 'secondary' {
    if (s === 'online') return 'success';
    if (s === 'away') return 'warning';
    return 'secondary';
  }
}
