import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ProfessionalsService } from './professionals.service';

/** Spec 011 US2 (T065) — formulário de criação/edição de profissional (name, specialty, dept, attendant). */
@Component({
  selector: 'app-professional-form',
  standalone: true,
  imports: [CommonModule, RouterLink, ReactiveFormsModule, InputTextModule, ButtonModule, ToastModule],
  providers: [MessageService],
  templateUrl: './professional-form.component.html',
  styleUrl: './professional-form.component.scss',
})
export class ProfessionalFormComponent implements OnInit {
  private readonly svc    = inject(ProfessionalsService);
  private readonly route  = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb     = inject(FormBuilder);
  private readonly toast  = inject(MessageService);

  isNew = signal(true);
  saving = signal(false);
  professionalId: string | null = null;

  form = this.fb.group({
    name:      ['', [Validators.required, Validators.maxLength(255)]],
    specialty: [null as string | null, [Validators.maxLength(100)]],
  });

  async ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'novo') {
      this.isNew.set(false);
      this.professionalId = id;
      const { items } = await this.svc.list({ includeInactive: true });
      const found = items.find(p => p.id === id);
      if (found) this.form.patchValue(found);
    }
  }

  async save() {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      if (this.isNew()) {
        await this.svc.create({ name: v.name!, specialty: v.specialty });
      } else {
        await this.svc.update(this.professionalId!, { name: v.name!, specialty: v.specialty });
      }
      this.toast.add({ severity: 'success', summary: 'Salvo' });
      await this.router.navigate(['..'], { relativeTo: this.route });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro ao salvar.' });
    } finally { this.saving.set(false); }
  }
}
