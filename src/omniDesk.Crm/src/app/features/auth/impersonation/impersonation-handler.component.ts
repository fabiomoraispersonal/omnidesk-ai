import { Component, OnInit, inject } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { TokenService } from '../../../core/services/token.service';

@Component({
  selector: 'app-impersonation-handler',
  standalone: true,
  template: `<p>Autenticando como operador...</p>`,
})
export class ImpersonationHandlerComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tokenService = inject(TokenService);
  private readonly authService = inject(AuthService);

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.router.navigate(['/']);
      return;
    }

    // Store token in memory, clear it from the URL
    this.tokenService.setToken(token);

    const payload = this.tokenService.decodePayload(token);
    if (payload) {
      this.authService.currentUser.set({
        id: payload['sub'],
        name: 'Operador SaaS',
        role: payload['role'],
        tenantSlug: payload['tenant_slug'],
        isImpersonation: true,
      });
    }

    // Replace URL to remove token from address bar
    history.replaceState(null, '', '/');
    this.router.navigate(['/'], { replaceUrl: true });
  }
}
