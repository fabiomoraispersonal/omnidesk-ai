import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { SelectModule } from 'primeng/select';
import { Department, DepartmentService } from '../../departments/services/department.service';
import { CannedResponseService } from '../services/canned-response.service';

@Component({
  selector: 'omni-canned-response-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, InputTextModule, TextareaModule, SelectModule],
  templateUrl: './canned-response-form.component.html',
})
export class CannedResponseFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(CannedResponseService);
  private readonly departments = inject(DepartmentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly editing = signal(false);
  protected readonly availableDepartments = signal<Department[]>([]);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.fb.group({
    title: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]),
    content: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(1), Validators.maxLength(4000)]),
    departmentId: new FormControl<string | null>(null),
  });

  protected readonly previewVariables = computed(() => {
    const matches = (this.form.controls.content.value ?? '').matchAll(/\{\{(\w+)\}\}/g);
    return Array.from(matches, m => m[1]);
  });

  ngOnInit(): void {
    this.departments.list(false).subscribe(d => this.availableDepartments.set(d));
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.editing.set(true);
      this.service.list().subscribe(rows => {
        const found = rows.find(r => r.id === id);
        if (found) this.form.patchValue({
          title: found.title,
          content: found.content,
          departmentId: found.departmentId,
        });
      });
    }
  }

  save(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const v = this.form.getRawValue();
    const payload = {
      title: v.title,
      content: v.content,
      departmentId: v.departmentId ?? null,
    };
    const id = this.route.snapshot.paramMap.get('id');
    const obs = id && id !== 'new'
      ? this.service.update(id, payload)
      : this.service.create(payload);
    obs.subscribe({
      next: () => this.router.navigate(['/canned-responses']),
      error: err => this.errorMessage.set(err?.error?.error?.message ?? 'Falha ao salvar.'),
    });
  }

  cancel(): void { this.router.navigate(['/canned-responses']); }
}
