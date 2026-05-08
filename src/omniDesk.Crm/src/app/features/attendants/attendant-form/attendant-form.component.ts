import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { Department, DepartmentService } from '../../departments/services/department.service';
import { Attendant, AttendantService } from '../services/attendant.service';

@Component({
  selector: 'omni-attendant-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, InputTextModule, InputNumberModule, MultiSelectModule, SelectModule],
  templateUrl: './attendant-form.component.html',
})
export class AttendantFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly attendants = inject(AttendantService);
  private readonly departments = inject(DepartmentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly editing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly availableDepartments = signal<Department[]>([]);
  protected readonly avatarPreview = signal<string | null>(null);
  private avatarFile: File | null = null;

  protected readonly form = this.fb.group({
    userId: this.fb.nonNullable.control('', [Validators.required]),
    name: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(2), Validators.maxLength(255)]),
    maxSimultaneousChats: this.fb.nonNullable.control(5, [Validators.min(1), Validators.max(100)]),
    departmentIds: this.fb.nonNullable.control<string[]>([]),
    primaryDepartmentId: new FormControl<string | null>(null),
  });

  ngOnInit(): void {
    this.departments.list().subscribe(d => this.availableDepartments.set(d));
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.editing.set(true);
      this.attendants.get(id).subscribe(a => this.populate(a));
    }
  }

  onAvatarSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) {
      this.errorMessage.set('Avatar não pode exceder 2 MB.');
      input.value = '';
      return;
    }
    this.avatarFile = file;
    const reader = new FileReader();
    reader.onload = () => this.avatarPreview.set(reader.result as string);
    reader.readAsDataURL(file);
  }

  save(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const v = this.form.getRawValue();
    if (v.primaryDepartmentId && !v.departmentIds.includes(v.primaryDepartmentId)) {
      this.errorMessage.set('O departamento principal precisa estar entre os selecionados.');
      return;
    }
    const id = this.route.snapshot.paramMap.get('id');
    const isEdit = !!id && id !== 'new';
    const obs = isEdit
      ? this.attendants.update(id!, { name: v.name, maxSimultaneousChats: v.maxSimultaneousChats })
      : this.attendants.create({
          userId: v.userId,
          name: v.name,
          maxSimultaneousChats: v.maxSimultaneousChats,
          departmentIds: v.departmentIds,
          primaryDepartmentId: v.primaryDepartmentId,
        });

    obs.subscribe({
      next: a => {
        if (isEdit) {
          this.attendants.updateDepartments(a.id, {
            departmentIds: v.departmentIds,
            primaryDepartmentId: v.primaryDepartmentId,
          }).subscribe(() => this.afterSave(a.id));
        } else {
          this.afterSave(a.id);
        }
      },
      error: err => this.errorMessage.set(err?.error?.error?.message ?? 'Falha ao salvar.'),
    });
  }

  cancel(): void { this.router.navigate(['/attendants']); }

  private afterSave(id: string): void {
    if (this.avatarFile) {
      this.attendants.uploadAvatar(id, this.avatarFile).subscribe({
        complete: () => this.router.navigate(['/attendants']),
      });
    } else {
      this.router.navigate(['/attendants']);
    }
  }

  private populate(a: Attendant): void {
    this.form.patchValue({
      userId: a.userId,
      name: a.name,
      maxSimultaneousChats: a.maxSimultaneousChats,
      departmentIds: a.departmentIds,
      primaryDepartmentId: a.primaryDepartmentId,
    });
    this.form.controls.userId.disable();
    this.avatarPreview.set(a.avatarUrl);
  }
}
