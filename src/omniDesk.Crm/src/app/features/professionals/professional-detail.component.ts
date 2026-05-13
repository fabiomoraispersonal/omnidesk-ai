import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TabViewModule } from 'primeng/tabview';
import { ButtonModule } from 'primeng/button';
import { ProfessionalServicesComponent } from './professional-services.component';
import { WeeklyScheduleComponent } from './weekly-schedule.component';
import { ScheduleBlocksComponent } from './schedule-blocks.component';

/** Spec 011 US2 (T069) — container de detalhe do profissional com tabs de sub-recursos. */
@Component({
  selector: 'app-professional-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, TabViewModule, ButtonModule,
            ProfessionalServicesComponent, WeeklyScheduleComponent, ScheduleBlocksComponent],
  template: `
    <div class="professional-detail-page">
      <div class="page-header">
        <h2>Configurar profissional</h2>
        <div class="header-actions">
          <a [routerLink]="['editar']">
            <p-button label="Editar dados" icon="pi pi-pencil" [text]="true" severity="secondary" />
          </a>
          <a routerLink="..">
            <p-button label="Voltar" icon="pi pi-arrow-left" [text]="true" severity="secondary" />
          </a>
        </div>
      </div>

      <p-tabView *ngIf="professionalId()">
        <p-tabPanel header="Serviços">
          <app-professional-services [professionalId]="professionalId()!" />
        </p-tabPanel>
        <p-tabPanel header="Agenda semanal">
          <app-weekly-schedule [professionalId]="professionalId()!" />
        </p-tabPanel>
        <p-tabPanel header="Bloqueios">
          <app-schedule-blocks [professionalId]="professionalId()!" />
        </p-tabPanel>
      </p-tabView>
    </div>
  `,
  styles: [`
    .professional-detail-page { padding: 1.5rem; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; h2 { margin: 0; font-size: 1.25rem; font-weight: 600; } }
    .header-actions { display: flex; gap: 0.5rem; }
  `],
})
export class ProfessionalDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  professionalId = signal<string | null>(null);

  ngOnInit() {
    this.professionalId.set(this.route.snapshot.paramMap.get('id'));
  }
}
