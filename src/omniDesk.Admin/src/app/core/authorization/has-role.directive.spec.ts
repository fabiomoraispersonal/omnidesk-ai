import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HasRoleDirective } from './has-role.directive';
import { ROLES, Role, RoleSignal } from './role.signal';

@Component({
  standalone: true,
  imports: [HasRoleDirective],
  template: `<span *omniHasRole="allowed">visible</span>`,
})
class HostComponent {
  allowed: Role | Role[] = ROLES.SaasAdmin;
}

describe('HasRoleDirective', () => {
  const roleValue = signal<Role | null>(null);
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    roleValue.set(null);
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: RoleSignal, useValue: { role: roleValue } }],
    }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
  });

  it('hides content when role is not allowed', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('visible');
  });

  it('shows content when role is allowed', () => {
    roleValue.set(ROLES.SaasAdmin);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('visible');
  });

  it('accepts an array of allowed roles', () => {
    fixture.componentInstance.allowed = [ROLES.TenantAdmin, ROLES.Supervisor];
    roleValue.set(ROLES.Supervisor);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('visible');
  });
});
