import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let service: ThemeService;

  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    TestBed.configureTestingModule({});
    service = TestBed.inject(ThemeService);
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it('starts in light mode when localStorage has no preference', () => {
    expect(service.isDark()).toBeFalse();
  });

  it('starts in dark mode when localStorage has "dark"', () => {
    localStorage.setItem('theme', 'dark');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const freshService = TestBed.inject(ThemeService);
    expect(freshService.isDark()).toBeTrue();
  });

  it('toggles from light to dark', () => {
    expect(service.isDark()).toBeFalse();
    service.toggle();
    expect(service.isDark()).toBeTrue();
  });

  it('toggles from dark to light', () => {
    localStorage.setItem('theme', 'dark');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const freshService = TestBed.inject(ThemeService);
    freshService.toggle();
    expect(freshService.isDark()).toBeFalse();
  });

  it('adds .dark class to <html> when toggling to dark', () => {
    service.toggle();
    expect(document.documentElement.classList.contains('dark')).toBeTrue();
  });

  it('removes .dark class from <html> when toggling to light', () => {
    service.toggle();
    service.toggle();
    expect(document.documentElement.classList.contains('dark')).toBeFalse();
  });

  it('persists dark preference to localStorage', () => {
    service.toggle();
    expect(localStorage.getItem('theme')).toBe('dark');
  });

  it('persists light preference to localStorage', () => {
    service.toggle();
    service.toggle();
    expect(localStorage.getItem('theme')).toBe('light');
  });
});
