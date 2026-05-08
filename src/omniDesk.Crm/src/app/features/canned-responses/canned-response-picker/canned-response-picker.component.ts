import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, debounceTime, switchMap, takeUntil } from 'rxjs';
import { ListboxModule } from 'primeng/listbox';
import { CannedResponse, CannedResponseService } from '../services/canned-response.service';

interface PickerInput {
  textareaRef: HTMLTextAreaElement;
  ticketId?: string;
  departmentId?: string;
  conversationId?: string;
}

@Component({
  selector: 'omni-canned-response-picker',
  standalone: true,
  imports: [CommonModule, ListboxModule],
  templateUrl: './canned-response-picker.component.html',
})
export class CannedResponsePickerComponent implements AfterViewInit, OnDestroy {
  private readonly service = inject(CannedResponseService);
  private readonly destroy$ = new Subject<void>();
  private readonly searchInput$ = new Subject<string>();

  @Input({ required: true }) input!: PickerInput;
  @Output() inserted = new EventEmitter<string>();

  @ViewChild('host') host!: ElementRef<HTMLElement>;
  protected readonly visible = signal(false);
  protected readonly results = signal<CannedResponse[]>([]);

  ngAfterViewInit(): void {
    this.input.textareaRef.addEventListener('input', this.onTextareaInput);
    this.searchInput$
      .pipe(
        debounceTime(150),
        switchMap(q => this.service.list({ q, departmentId: this.input.departmentId })),
        takeUntil(this.destroy$),
      )
      .subscribe(rows => this.results.set(rows.slice(0, 10)));
  }

  ngOnDestroy(): void {
    this.input.textareaRef.removeEventListener('input', this.onTextareaInput);
    this.destroy$.next();
    this.destroy$.complete();
  }

  protected onSelect(template: CannedResponse): void {
    this.service.render(template.id, {
      ticketId: this.input.ticketId,
      conversationId: this.input.conversationId,
    }).subscribe(({ rendered }) => {
      // Replace the trailing "/query" with the rendered text.
      const ta = this.input.textareaRef;
      const value = ta.value;
      const slashIndex = value.lastIndexOf('/');
      if (slashIndex >= 0) {
        ta.value = value.slice(0, slashIndex) + rendered;
        ta.dispatchEvent(new Event('input', { bubbles: true }));
      } else {
        ta.value = `${value}${rendered}`;
      }
      this.visible.set(false);
      this.inserted.emit(rendered);
    });
  }

  private readonly onTextareaInput = (event: Event) => {
    const value = (event.target as HTMLTextAreaElement).value;
    const slashMatch = value.match(/\/(\w*)$/);
    if (slashMatch) {
      this.visible.set(true);
      this.searchInput$.next(slashMatch[1]);
    } else {
      this.visible.set(false);
    }
  };
}
