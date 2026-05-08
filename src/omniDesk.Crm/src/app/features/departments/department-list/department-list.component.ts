import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { Department, DepartmentService } from '../services/department.service';

@Component({
  selector: 'omni-department-list',
  standalone: true,
  imports: [CommonModule, TableModule, ButtonModule, TagModule],
  templateUrl: './department-list.component.html',
})
export class DepartmentListComponent implements OnInit {
  private readonly service = inject(DepartmentService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly departments = signal<Department[]>([]);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.service.list(false).subscribe({
      next: list => { this.departments.set(list); this.loading.set(false); },
      error: () => { this.errorMessage.set('Não foi possível carregar departamentos.'); this.loading.set(false); },
    });
  }

  newDepartment(): void { this.router.navigate(['/departments/new']); }
  edit(id: string): void { this.router.navigate(['/departments', id, 'edit']); }
  deactivate(id: string): void {
    if (!confirm('Tem certeza? O departamento será desativado.')) return;
    this.service.deactivate(id).subscribe(() => this.refresh());
  }
}
