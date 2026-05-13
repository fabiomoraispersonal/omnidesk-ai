import { Component, OnInit, inject, signal, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { CheckboxModule } from 'primeng/checkbox';
import { FormsModule } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { ProfessionalsService } from './professionals.service';
import { ServicesService, ServiceDto } from '../services-catalog/services.service';

/** Spec 011 US2 (T066) — seleciona os serviços oferecidos por um profissional. */
@Component({
  selector: 'app-professional-services',
  standalone: true,
  imports: [CommonModule, FormsModule, CheckboxModule, ButtonModule, ToastModule],
  providers: [MessageService],
  templateUrl: './professional-services.component.html',
  styleUrl: './professional-services.component.scss',
})
export class ProfessionalServicesComponent implements OnInit {
  @Input({ required: true }) professionalId!: string;

  private readonly profSvc = inject(ProfessionalsService);
  private readonly svcCatalog = inject(ServicesService);
  private readonly toast = inject(MessageService);

  allServices = signal<ServiceDto[]>([]);
  linkedIds = signal<string[]>([]);
  saving = signal(false);

  async ngOnInit() {
    const [catalog, linked] = await Promise.all([
      this.svcCatalog.list({ includeInactive: false }),
      this.profSvc.getServices(this.professionalId),
    ]);
    this.allServices.set(catalog.items);
    this.linkedIds.set(linked.map(s => s.id));
  }

  isLinked(id: string) { return this.linkedIds().includes(id); }

  toggle(id: string) {
    const current = this.linkedIds();
    this.linkedIds.set(current.includes(id) ? current.filter(x => x !== id) : [...current, id]);
  }

  async save() {
    this.saving.set(true);
    try {
      await this.profSvc.updateServices(this.professionalId, this.linkedIds());
      this.toast.add({ severity: 'success', summary: 'Serviços atualizados' });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro ao salvar.' });
    } finally { this.saving.set(false); }
  }
}
