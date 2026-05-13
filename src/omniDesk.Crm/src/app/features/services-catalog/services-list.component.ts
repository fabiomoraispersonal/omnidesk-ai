import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { ToggleButtonModule } from 'primeng/togglebutton';
import { FormsModule } from '@angular/forms';
import { ServiceDto, ServicesCatalogService } from './services.service';

/**
 * Spec 011 US1 (T038) — lista de serviços do catálogo com filtro ativo/inativo,
 * sort e botões de editar/ativar/desativar.
 */
@Component({
  selector: 'app-services-list',
  standalone: true,
  imports: [CommonModule, RouterLink, TableModule, ButtonModule, TagModule, ToggleButtonModule, FormsModule],
  templateUrl: './services-list.component.html',
  styleUrl: './services-list.component.scss',
})
export class ServicesListComponent implements OnInit {
  private readonly svc = inject(ServicesCatalogService);

  items = signal<ServiceDto[]>([]);
  total = signal(0);
  loading = signal(false);
  showInactive = signal(false);

  async ngOnInit() {
    await this.load();
  }

  async load() {
    this.loading.set(true);
    try {
      const { items, total } = await this.svc.list({ includeInactive: this.showInactive() });
      this.items.set(items);
      this.total.set(total);
    } finally {
      this.loading.set(false);
    }
  }

  async toggleActive(item: ServiceDto) {
    await this.svc.toggle(item.id, !item.is_active);
    await this.load();
  }

  formatPrice(price: number | null): string {
    if (price === null) return 'A combinar';
    return price.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  }
}
