import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SlaCountdownComponent } from './sla-countdown.component';
import { SlaInfo } from '../../tickets-kanban/services/tickets.service';

// ---------------------------------------------------------------------------
// Spec 009 US3 — T124
// Smoke tests for SlaCountdownComponent.
// ---------------------------------------------------------------------------

describe('SlaCountdownComponent', () => {
  let fixture: ComponentFixture<SlaCountdownComponent>;
  let component: SlaCountdownComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SlaCountdownComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(SlaCountdownComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders nothing when sla input is null', () => {
    fixture.componentRef.setInput('sla', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.sla-countdown')).toBeNull();
  });

  it('renders countdown for ok status', () => {
    const future = new Date(Date.now() + 60 * 60 * 1000).toISOString(); // 1h from now
    const sla: SlaInfo = {
      status: 'ok',
      resolution_deadline_effective: future,
    };
    fixture.componentRef.setInput('sla', sla);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.sla-countdown')).toBeTruthy();
    expect(el.querySelector('.sla-ok')).toBeTruthy();
  });

  it('shows breached class for expired deadline', () => {
    const past = new Date(Date.now() - 5 * 60 * 1000).toISOString(); // 5min ago
    const sla: SlaInfo = {
      status: 'breached',
      resolution_deadline_effective: past,
    };
    fixture.componentRef.setInput('sla', sla);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const timeEl = el.querySelector('.sla-time') as HTMLElement | null;
    expect(timeEl?.classList.contains('breached')).toBeTrue();
  });

  it('shows warning class for warning status', () => {
    const soon = new Date(Date.now() + 5 * 60 * 1000).toISOString(); // 5min from now
    const sla: SlaInfo = {
      status: 'warning',
      resolution_deadline_effective: soon,
    };
    fixture.componentRef.setInput('sla', sla);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.sla-warning')).toBeTruthy();
  });

  it('shows "Restam" label when not breached', () => {
    const future = new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString();
    const sla: SlaInfo = { status: 'ok', resolution_deadline_effective: future };
    fixture.componentRef.setInput('sla', sla);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Restam');
  });

  it('shows "SLA vencido" label when breached', () => {
    const past = new Date(Date.now() - 10 * 60 * 1000).toISOString();
    const sla: SlaInfo = { status: 'breached', resolution_deadline_effective: past };
    fixture.componentRef.setInput('sla', sla);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('SLA vencido');
  });
});
