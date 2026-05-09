import { ChangeDetectionStrategy, Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-prompt-variables-helper',
  standalone: true,
  imports: [CommonModule, ButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="variables-helper">
      <small class="muted">Inserir variável:</small>
      <p-button
        *ngFor="let v of variables"
        size="small"
        [text]="true"
        [label]="'{{' + v + '}}'"
        (onClick)="insert.emit('{{' + v + '}}')">
      </p-button>
    </div>
  `,
  styles: [`
    .variables-helper { display: flex; flex-wrap: wrap; gap: .25rem; align-items: center; }
    .muted { color: var(--text-color-secondary, #888); margin-right: .5rem; }
  `],
})
export class PromptVariablesHelperComponent {
  readonly variables = ['company_name', 'department_name', 'attendant_name'];

  @Output() insert = new EventEmitter<string>();
}
