import {
  Directive,
  EmbeddedViewRef,
  Input,
  TemplateRef,
  ViewContainerRef,
  effect,
  inject,
} from '@angular/core';
import { Role, RoleSignal } from './role.signal';

@Directive({
  selector: '[omniHasRole]',
  standalone: true,
})
export class HasRoleDirective {
  private readonly template = inject(TemplateRef<unknown>);
  private readonly viewContainer = inject(ViewContainerRef);
  private readonly roleSignal = inject(RoleSignal);
  private allowed: Role[] = [];
  private viewRef: EmbeddedViewRef<unknown> | null = null;

  constructor() {
    effect(() => this.refresh(this.roleSignal.role()));
  }

  @Input({ required: true })
  set omniHasRole(value: Role | Role[] | null | undefined) {
    this.allowed = !value ? [] : Array.isArray(value) ? value : [value];
    this.refresh(this.roleSignal.role());
  }

  private refresh(current: Role | null): void {
    const shouldShow = current !== null && this.allowed.includes(current);
    if (shouldShow && !this.viewRef) {
      this.viewRef = this.viewContainer.createEmbeddedView(this.template);
    } else if (!shouldShow && this.viewRef) {
      this.viewContainer.clear();
      this.viewRef = null;
    }
  }
}
