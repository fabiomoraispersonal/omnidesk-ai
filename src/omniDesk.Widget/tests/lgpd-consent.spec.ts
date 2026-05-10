// Spec 007 — LGPD consent component unit tests.

import { describe, it, expect, vi } from 'vitest';
import { createLgpdConsent } from '../src/ui/lgpd-consent';

describe('createLgpdConsent', () => {
  it('renders a checkbox + privacy link when URL is provided', () => {
    const onConsent = vi.fn();
    const el = createLgpdConsent({ text: 'Aceito', url: 'https://privacy.example' }, onConsent);

    const checkbox = el.querySelector('input[type="checkbox"]') as HTMLInputElement;
    const link = el.querySelector('a') as HTMLAnchorElement;
    expect(checkbox).toBeTruthy();
    expect(link).toBeTruthy();
    expect(link.href).toContain('privacy.example');
  });

  it('emits consent only after the checkbox is checked', () => {
    const onConsent = vi.fn();
    const el = createLgpdConsent({ text: 'Aceito', url: null }, onConsent);
    const checkbox = el.querySelector('input[type="checkbox"]') as HTMLInputElement;

    expect(onConsent).not.toHaveBeenCalled();
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    expect(onConsent).toHaveBeenCalledOnce();
  });

  it('uses the default text when none is provided', () => {
    const onConsent = vi.fn();
    const el = createLgpdConsent({ text: null, url: null }, onConsent);
    expect(el.textContent).toMatch(/LGPD/i);
  });
});
