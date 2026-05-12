// Spec 009 US7 — T162
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-search-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, InputTextModule, ButtonModule],
  template: `
    <div class="search-bar">
      <span class="p-input-icon-left search-input-wrap">
        <i class="pi pi-search"></i>
        <input
          pInputText
          type="text"
          placeholder="Buscar ticket (protocolo, assunto, contato)..."
          [value]="value()"
          (input)="onInput($any($event.target).value)"
          style="width: 320px"
        />
      </span>
      @if (value()) {
        <p-button
          icon="pi pi-times"
          severity="secondary"
          [text]="true"
          size="small"
          (onClick)="clear()"
          pTooltip="Limpar busca"
        ></p-button>
      }
    </div>
  `,
  styles: [`
    .search-bar { display: flex; align-items: center; gap: 4px; }
    .search-input-wrap { flex: 1; }
  `],
})
export class SearchBarComponent {
  @Output() searched = new EventEmitter<string>();

  readonly value = signal('');
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;

  onInput(raw: string): void {
    this.value.set(raw);
    if (this.debounceTimer) clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => {
      const v = raw.trim();
      if (v.length === 0 || v.length >= 3) {
        this.searched.emit(v);
      }
    }, 300);
  }

  clear(): void {
    this.value.set('');
    if (this.debounceTimer) clearTimeout(this.debounceTimer);
    this.searched.emit('');
  }
}
