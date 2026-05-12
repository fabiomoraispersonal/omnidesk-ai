// Spec 009 US2 — PrimeNG Chips-based tags editor with debounced emit.
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChipsModule } from 'primeng/chips';

@Component({
  selector: 'app-tags-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, ChipsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tags-editor">
      <span class="field-label">Tags</span>
      <p-chips
        [(ngModel)]="tags"
        (onAdd)="onChanged()"
        (onRemove)="onChanged()"
        [allowDuplicate]="false"
        placeholder="Adicionar tag"
      />
    </div>
  `,
  styles: [`
    .tags-editor { display: flex; flex-direction: column; gap: 4px; }
    .field-label { font-size: 11px; font-weight: 600; color: #7A7A7A; text-transform: uppercase; letter-spacing: 0.5px; }
  `],
})
export class TagsEditorComponent implements OnInit {
  readonly currentTags = input<string[]>([]);
  readonly tagsChange = output<string[]>();

  tags: string[] = [];

  private debounceTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.tags = [...this.currentTags()];
  }

  onChanged(): void {
    if (this.debounceTimer) clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => {
      this.tagsChange.emit([...this.tags]);
    }, 400);
  }
}
