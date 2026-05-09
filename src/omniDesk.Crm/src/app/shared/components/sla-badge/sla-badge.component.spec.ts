import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SlaBadgeComponent, SlaSnapshot } from './sla-badge.component';

describe('SlaBadgeComponent', () => {
  let fixture: ComponentFixture<SlaBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SlaBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(SlaBadgeComponent);
  });

  function setSnapshot(snap: SlaSnapshot) {
    fixture.componentInstance.snapshot = snap;
    fixture.detectChanges();
  }

  it('hides badge when SLA is not configured', () => {
    setSnapshot({
      firstResponseMinutes: null, resolutionMinutes: null,
      firstResponseElapsedMinutes: null, resolutionElapsedMinutes: null,
      firstResponseStatus: 'not_configured', resolutionStatus: 'not_configured',
    });
    expect((fixture.componentInstance as any).displayed()).toBeNull();
  });

  it('renders danger badge when any phase is overdue', () => {
    setSnapshot({
      firstResponseMinutes: 30, resolutionMinutes: 240,
      firstResponseElapsedMinutes: 35, resolutionElapsedMinutes: 60,
      firstResponseStatus: 'overdue', resolutionStatus: 'ok',
    });
    const cmp = fixture.componentInstance as any;
    expect(cmp.displayed().severity).toBe('danger');
  });

  it('renders warning when only warning is reached', () => {
    setSnapshot({
      firstResponseMinutes: 30, resolutionMinutes: 240,
      firstResponseElapsedMinutes: 25, resolutionElapsedMinutes: 60,
      firstResponseStatus: 'warning', resolutionStatus: 'ok',
    });
    expect((fixture.componentInstance as any).displayed().severity).toBe('warning');
  });

  it('renders ok when nothing is past 80%', () => {
    setSnapshot({
      firstResponseMinutes: 30, resolutionMinutes: 240,
      firstResponseElapsedMinutes: 5, resolutionElapsedMinutes: 60,
      firstResponseStatus: 'ok', resolutionStatus: 'ok',
    });
    expect((fixture.componentInstance as any).displayed().severity).toBe('success');
  });
});
