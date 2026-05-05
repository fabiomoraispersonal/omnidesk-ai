import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  inject,
  OnDestroy,
  Output,
  ViewChild,
} from '@angular/core';
import { TURNSTILE_SITE_KEY } from '../../../core/tokens/turnstile.tokens';

declare const turnstile: {
  render(el: HTMLElement, options: Record<string, unknown>): string;
  remove(widgetId: string): void;
};

@Component({
  selector: 'app-turnstile',
  standalone: true,
  template: `<div #widget></div>`,
})
export class TurnstileComponent implements AfterViewInit, OnDestroy {
  @ViewChild('widget') widgetEl!: ElementRef<HTMLDivElement>;
  @Output() tokenChange = new EventEmitter<string | null>();

  private widgetId: string | null = null;
  private readonly siteKey = inject(TURNSTILE_SITE_KEY);

  ngAfterViewInit(): void {
    if (typeof turnstile === 'undefined') return;
    this.widgetId = turnstile.render(this.widgetEl.nativeElement, {
      sitekey: this.siteKey,
      theme: 'auto',
      callback: (token: string) => this.tokenChange.emit(token),
      'expired-callback': () => this.tokenChange.emit(null),
      'error-callback': () => this.tokenChange.emit(null),
    });
  }

  ngOnDestroy(): void {
    if (this.widgetId && typeof turnstile !== 'undefined') {
      turnstile.remove(this.widgetId);
    }
  }
}
