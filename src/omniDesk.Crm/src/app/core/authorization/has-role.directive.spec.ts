import { Component, signal } from '@angular/core';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HasRoleDirective } from './has-role.directive';
import { ROLES, Role, RoleSignal } from './role.signal';

@Component({
  standalone: true,
  imports: [HasRoleDirective],
  template: `<span *omniHasRole="allowed">visible</span>`,
})
class HostComponent {
  allowed: Role | Role[] = ROLES.Supervisor;
}

describe('HasRoleDirective (CRM)', () => {
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

  it('hides until allowed', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('visible');
    roleValue.set(ROLES.Supervisor);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('visible');
  });

  it('honors arrays of roles', () => {
    fixture.componentInstance.allowed = [ROLES.TenantAdmin, ROLES.Supervisor];
    roleValue.set(ROLES.TenantAdmin);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('visible');
  });
});
