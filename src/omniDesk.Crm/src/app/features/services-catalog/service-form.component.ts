import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { InputNumberModule } from 'primeng/inputnumber';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ServicesCatalogService } from './services.service';

/**
 * Spec 011 US1 (T040) — formulário de criação e edição de serviço.
 * Rota: /configuracoes/servicos/novo  (novo)
 * Rota: /configuracoes/servicos/:id   (editar)
 */
@Component({
  selector: 'app-service-form',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
    InputTextModule, InputTextareaModule, InputNumberModule,
    ToggleSwitchModule, ButtonModule, ToastModule,
  ],
  providers: [MessageService],
  templateUrl: './service-form.component.html',
  styleUrl: './service-form.component.scss',
})
export class ServiceFormComponent implements OnInit {
  private readonly svc    = inject(ServicesCatalogService);
  private readonly route  = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb     = inject(FormBuilder);
  private readonly toast  = inject(MessageService);

  isNew = signal(true);
  saving = signal(false);
  serviceId: string | null = null;

  form = this.fb.group({
    name:                  ['', [Validators.required, Validators.maxLength(100)]],
    description:           [null as string | null],
    category:              [null as string | null, [Validators.maxLength(100)]],
    duration_minutes:      [null as number | null, [Validators.required, Validators.min(1)]],
    price:                 [null as number | null, [Validators.min(0)]],
    requires_confirmation: [false],
  });

  async ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'novo') {
      this.isNew.set(false);
      this.serviceId = id;
      // If navigated with state data, skip fetch.
      const state = history.state?.service;
      if (state) { this.patchForm(state); } else { await this.fetchService(id); }
    }
  }

  private patchForm(s: { name: string; description: string | null; category: string | null;
    duration_minutes: number; price: number | null; requires_confirmation: boolean }) {
    this.form.patchValue(s);
  }

  private async fetchService(id: string) {
    const { items } = await this.svc.list({ includeInactive: true });
    const found = items.find(s => s.id === id);
    if (found) this.patchForm(found);
  }

  async save() {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      const req = {
        name:                  v.name!,
        description:           v.description ?? null,
        category:              v.category ?? null,
        duration_minutes:      v.duration_minutes!,
        price:                 v.price ?? null,
        requires_confirmation: v.requires_confirmation ?? false,
      };

      if (this.isNew()) {
        await this.svc.create(req);
      } else {
        await this.svc.update(this.serviceId!, req);
      }

      this.toast.add({ severity: 'success', summary: 'Salvo', detail: 'Serviço salvo com sucesso.' });
      await this.router.navigate(['..'], { relativeTo: this.route });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível salvar o serviço.' });
    } finally {
      this.saving.set(false);
    }
  }
}
