import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { PasswordModule } from 'primeng/password';
import { CardModule } from 'primeng/card';
import { MessageModule } from 'primeng/message';
import { BRAZILIAN_TIMEZONES } from '../../../core/constants/timezones';
import { cnpjValidator } from '../../../shared/validators/cnpj.validator';
import { TenantService } from '../services/tenant.service';

@Component({
  selector: 'app-tenant-create',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    ButtonModule, InputTextModule, SelectModule, PasswordModule, CardModule, MessageModule,
  ],
  template: `
    <p-card header="Novo Tenant">
      <form [formGroup]="form" (ngSubmit)="submit()">
        <div class="grid grid-cols-2 gap-4">
          <div class="flex flex-col gap-1">
            <label>Slug *</label>
            <input pInputText formControlName="slug" placeholder="clinica-abc" />
          </div>
          <div class="flex flex-col gap-1">
            <label>Razão Social *</label>
            <input pInputText formControlName="razaoSocial" />
          </div>
          <div class="flex flex-col gap-1">
            <label>Nome Fantasia</label>
            <input pInputText formControlName="nomeFantasia" />
          </div>
          <div class="flex flex-col gap-1">
            <label>CNPJ *</label>
            <input pInputText formControlName="cnpj" placeholder="00.000.000/0001-00" />
          </div>
          <div class="flex flex-col gap-1">
            <label>Fuso Horário *</label>
            <p-select formControlName="timezone" [options]="timezones" placeholder="Selecione" />
          </div>
        </div>

        <fieldset class="mt-4 border rounded p-4">
          <legend class="font-semibold">Contato Financeiro</legend>
          <div formGroupName="financialContact" class="grid grid-cols-3 gap-4">
            <div class="flex flex-col gap-1">
              <label>Nome *</label>
              <input pInputText formControlName="name" />
            </div>
            <div class="flex flex-col gap-1">
              <label>E-mail *</label>
              <input pInputText formControlName="email" type="email" />
            </div>
            <div class="flex flex-col gap-1">
              <label>Telefone *</label>
              <input pInputText formControlName="phone" />
            </div>
          </div>
        </fieldset>

        <fieldset class="mt-4 border rounded p-4">
          <legend class="font-semibold">Responsável Técnico (Super Admin)</legend>
          <div formGroupName="technicalContact" class="grid grid-cols-3 gap-4">
            <div class="flex flex-col gap-1">
              <label>Nome *</label>
              <input pInputText formControlName="name" />
            </div>
            <div class="flex flex-col gap-1">
              <label>E-mail *</label>
              <input pInputText formControlName="email" type="email" />
            </div>
            <div class="flex flex-col gap-1">
              <label>Telefone *</label>
              <input pInputText formControlName="phone" />
            </div>
          </div>
        </fieldset>

        <fieldset class="mt-4 border rounded p-4">
          <legend class="font-semibold">OpenAI (opcional)</legend>
          <div class="grid grid-cols-3 gap-4">
            <div class="flex flex-col gap-1">
              <label>API Key</label>
              <p-password formControlName="openAiApiKey" [feedback]="false" [toggleMask]="true" />
            </div>
            <div class="flex flex-col gap-1">
              <label>Organization ID</label>
              <input pInputText formControlName="openAiOrganization" />
            </div>
            <div class="flex flex-col gap-1">
              <label>Project ID</label>
              <input pInputText formControlName="openAiProject" />
            </div>
          </div>
        </fieldset>

        @if (error) {
          <p-message severity="error" [text]="error" class="mt-4 block" />
        }

        <div class="flex gap-2 mt-6">
          <p-button label="Criar Tenant" type="submit" [loading]="loading" [disabled]="form.invalid" />
          <p-button label="Cancelar" severity="secondary" (onClick)="router.navigate(['/tenants'])" />
        </div>
      </form>
    </p-card>
  `,
})
export class TenantCreateComponent {
  protected readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly tenantService = inject(TenantService);

  protected readonly timezones = BRAZILIAN_TIMEZONES;
  protected loading = false;
  protected error: string | null = null;

  protected form = this.fb.group({
    slug: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]{3,50}$/)]],
    razaoSocial: ['', Validators.required],
    nomeFantasia: [''],
    cnpj: ['', [Validators.required, cnpjValidator()]],
    timezone: ['America/Sao_Paulo', Validators.required],
    financialContact: this.fb.group({
      name: ['', Validators.required],
      email: ['', [Validators.required, Validators.email]],
      phone: ['', Validators.required],
    }),
    technicalContact: this.fb.group({
      name: ['', Validators.required],
      email: ['', [Validators.required, Validators.email]],
      phone: ['', Validators.required],
    }),
    openAiApiKey: [''],
    openAiOrganization: [''],
    openAiProject: [''],
  });

  protected submit(): void {
    if (this.form.invalid) return;
    this.loading = true;
    this.error = null;

    const v = this.form.value;
    this.tenantService.createTenant({
      slug: v.slug!,
      razao_social: v.razaoSocial!,
      nome_fantasia: v.nomeFantasia || undefined,
      cnpj: v.cnpj!,
      timezone: v.timezone!,
      financial_contact: {
        name: v.financialContact!.name!,
        email: v.financialContact!.email!,
        phone: v.financialContact!.phone!,
      },
      technical_contact: {
        name: v.technicalContact!.name!,
        email: v.technicalContact!.email!,
        phone: v.technicalContact!.phone!,
      },
      openai_api_key: v.openAiApiKey || undefined,
      openai_organization: v.openAiOrganization || undefined,
      openai_project: v.openAiProject || undefined,
    }).subscribe({
      next: () => this.router.navigate(['/tenants']),
      error: (err) => {
        this.error = err?.error?.message ?? 'Erro ao criar tenant.';
        this.loading = false;
      },
    });
  }
}
