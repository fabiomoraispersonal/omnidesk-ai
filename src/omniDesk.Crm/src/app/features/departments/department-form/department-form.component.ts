import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { CheckboxModule } from 'primeng/checkbox';
import { Department, DepartmentService } from '../services/department.service';

const DAYS = [
  { value: 0, label: 'Dom' },
  { value: 1, label: 'Seg' },
  { value: 2, label: 'Ter' },
  { value: 3, label: 'Qua' },
  { value: 4, label: 'Qui' },
  { value: 5, label: 'Sex' },
  { value: 6, label: 'Sáb' },
];

@Component({
  selector: 'omni-department-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, InputTextModule, InputNumberModule, CheckboxModule],
  templateUrl: './department-form.component.html',
})
export class DepartmentFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(DepartmentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly days = DAYS;
  protected readonly editing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.fb.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]),
    description: new FormControl<string>(''),
    enableBusinessHours: this.fb.nonNullable.control(false),
    businessStart: this.fb.nonNullable.control('08:00'),
    businessEnd: this.fb.nonNullable.control('18:00'),
    selectedDays: this.fb.nonNullable.control<number[]>([1, 2, 3, 4, 5]),
    slaFirstResponse: new FormControl<number | null>(null),
    slaResolution: new FormControl<number | null>(null),
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.editing.set(true);
      this.service.get(id).subscribe(d => this.populate(d));
    }
  }

  toggleDay(day: number): void {
    const current = this.form.controls.selectedDays.value;
    const next = current.includes(day) ? current.filter(d => d !== day) : [...current, day].sort();
    this.form.controls.selectedDays.setValue(next);
  }

  isDaySelected(day: number): boolean {
    return this.form.controls.selectedDays.value.includes(day);
  }

  save(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const v = this.form.getRawValue();
    const payload = {
      name: v.name,
      description: v.description?.trim() || null,
      businessHours: v.enableBusinessHours
        ? { start: v.businessStart, end: v.businessEnd, days: v.selectedDays }
        : null,
      sla: {
        firstResponseMinutes: v.slaFirstResponse,
        resolutionMinutes: v.slaResolution,
      },
    };

    const id = this.route.snapshot.paramMap.get('id');
    const obs = id && id !== 'new'
      ? this.service.update(id, payload)
      : this.service.create(payload);

    obs.subscribe({
      next: () => this.router.navigate(['/departments']),
      error: err => this.errorMessage.set(err?.error?.error?.message ?? 'Falha ao salvar.'),
    });
  }

  cancel(): void { this.router.navigate(['/departments']); }

  private populate(d: Department): void {
    this.form.patchValue({
      name: d.name,
      description: d.description ?? '',
      enableBusinessHours: !!d.businessHours,
      businessStart: d.businessHours?.start ?? '08:00',
      businessEnd: d.businessHours?.end ?? '18:00',
      selectedDays: d.businessHours?.days ?? [1, 2, 3, 4, 5],
      slaFirstResponse: d.sla?.firstResponseMinutes ?? null,
      slaResolution: d.sla?.resolutionMinutes ?? null,
    });
  }
}
