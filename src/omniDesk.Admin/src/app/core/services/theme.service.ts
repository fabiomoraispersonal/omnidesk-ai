import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly isDark = signal<boolean>(
    typeof localStorage !== 'undefined' &&
      localStorage.getItem(STORAGE_KEY) === 'dark',
  );

  toggle(): void {
    const next = !this.isDark();
    this.isDark.set(next);
    document.documentElement.classList.toggle('dark', next);
    localStorage.setItem(STORAGE_KEY, next ? 'dark' : 'light');
  }

  applyFromStorage(): void {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'dark') {
      this.isDark.set(true);
      document.documentElement.classList.add('dark');
    }
  }
}
