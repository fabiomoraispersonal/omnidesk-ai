import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { ProfessionalDto, ProfessionalsService } from './professionals.service';

/** Spec 011 US2 (T064) — lista de profissionais com ações de editar e ativar/desativar. */
@Component({
  selector: 'app-professionals-list',
  standalone: true,
  imports: [CommonModule, RouterLink, TableModule, ButtonModule, TagModule],
  templateUrl: './professionals-list.component.html',
  styleUrl: './professionals-list.component.scss',
})
export class ProfessionalsListComponent implements OnInit {
  private readonly svc = inject(ProfessionalsService);

  items = signal<ProfessionalDto[]>([]);
  loading = signal(false);

  async ngOnInit() { await this.load(); }

  async load() {
    this.loading.set(true);
    try {
      const { items } = await this.svc.list({ includeInactive: true });
      this.items.set(items);
    } finally { this.loading.set(false); }
  }

  async toggleActive(p: ProfessionalDto) {
    await this.svc.toggle(p.id, !p.is_active);
    await this.load();
  }
}
