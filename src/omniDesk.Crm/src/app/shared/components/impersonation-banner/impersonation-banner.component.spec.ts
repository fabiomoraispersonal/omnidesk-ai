import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { ImpersonationBannerComponent } from './impersonation-banner.component';
import { AuthService, AuthUser } from '../../../core/services/auth.service';
import { TokenService } from '../../../core/services/token.service';

describe('ImpersonationBannerComponent', () => {
  const userSignal = signal<AuthUser | null>(null);
  const futureExp = Math.floor(Date.now() / 1000) + 305;
  let fixture: ComponentFixture<ImpersonationBannerComponent>;
  const tokenService = {
    getToken: () => 'fake.jwt.token',
    decodePayload: () => ({ exp: futureExp }),
  };
  const authService = {
    currentUser: userSignal,
    logout: jasmine.createSpy('logout').and.returnValue(of(undefined)),
  };
  const router = { navigate: jasmine.createSpy('navigate') };

  beforeEach(async () => {
    userSignal.set(null);
    authService.logout.calls.reset();
    router.navigate.calls.reset();
    await TestBed.configureTestingModule({
      imports: [ImpersonationBannerComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: TokenService, useValue: tokenService },
        { provide: Router, useValue: router },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ImpersonationBannerComponent);
  });

  it('hides banner when not impersonating', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.impersonation-banner')).toBeNull();
  });

  it('renders banner when impersonating', () => {
    userSignal.set({ id: 'u', name: '', role: 'saas_admin', isImpersonation: true });
    fixture.detectChanges();
    const el = fixture.nativeElement.querySelector('.impersonation-banner');
    expect(el).not.toBeNull();
    expect(el.textContent).toContain('admin SaaS');
  });

  it('renders MM:SS countdown', () => {
    userSignal.set({ id: 'u', name: '', role: 'saas_admin', isImpersonation: true });
    fixture.detectChanges();
    const el = fixture.nativeElement.querySelector('.impersonation-banner');
    expect(el.textContent).toMatch(/\d{2}:\d{2}/);
  });

  it('Encerrar agora triggers logout and redirects', () => {
    userSignal.set({ id: 'u', name: '', role: 'saas_admin', isImpersonation: true });
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('button');
    btn.click();
    expect(authService.logout).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
