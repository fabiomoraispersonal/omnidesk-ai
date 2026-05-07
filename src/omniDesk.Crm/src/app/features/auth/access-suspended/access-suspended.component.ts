import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-access-suspended',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex flex-col items-center justify-center min-h-screen p-4">
      <h1 class="text-3xl font-bold text-red-600 mb-2">Acesso suspenso</h1>
      <p class="text-gray-600">Entre em contato com o suporte.</p>
    </div>
  `,
})
export class AccessSuspendedComponent {}
