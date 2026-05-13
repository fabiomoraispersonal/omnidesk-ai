import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, RouterOutlet } from '@angular/router';
import { TabViewModule } from 'primeng/tabview';
import { ButtonModule } from 'primeng/button';

/**
 * Spec 011 US3 (T108) — page shell for the agenda feature.
 * Has tabs: Grade semanal / Lista / Pendentes.
 * Inner components are lazy-loaded via router outlet.
 */
@Component({
  selector: 'app-agenda-page',
  standalone: true,
  imports: [CommonModule, RouterModule, RouterOutlet, TabViewModule, ButtonModule],
  templateUrl: './agenda-page.component.html',
  styleUrls: ['./agenda-page.component.scss'],
})
export class AgendaPageComponent {}
