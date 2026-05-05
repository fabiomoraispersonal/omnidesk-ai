import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { providePrimeNG } from 'primeng/config';
import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';
import { provideNgxMask } from 'ngx-mask';
import { routes } from './app.routes';
import { environment } from '../environments/environment';
import { TURNSTILE_SITE_KEY } from './core/tokens/turnstile.tokens';

registerLocaleData(localePt);

const OmniDeskPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50:  '{color.primary.50}',
      100: '{color.primary.100}',
      200: '{color.primary.200}',
      300: '{color.primary.300}',
      400: '{color.primary.400}',
      500: '#6F7D5C',
      600: '#5E6B4E',
      700: '#4A563E',
      800: '#37402F',
      900: '#242A1F',
    },
    colorScheme: {
      light: {
        surface: {
          0:   '#FFFFFF',
          50:  '#F4F1EC',
          100: '#EDE7DF',
        },
      },
      dark: {
        surface: {
          0:   '#2A2A2A',
          50:  '#1E1E1E',
          100: '#333333',
        },
      },
    },
  },
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([])),
    provideAnimationsAsync(),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
    providePrimeNG({
      theme: {
        preset: OmniDeskPreset,
        options: { darkModeSelector: '.dark' },
      },
    }),
    provideNgxMask({ dropSpecialCharacters: true }),
    { provide: TURNSTILE_SITE_KEY, useValue: environment.turnstileSiteKey },
  ],
};
