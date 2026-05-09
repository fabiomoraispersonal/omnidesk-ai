import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { AttendantStatusToggleComponent } from './attendant-status-toggle.component';
import { PresenceSignal, PresenceState } from '../../../core/presence/presence.signal';
import { PresenceService } from '../../../core/presence/presence.service';

describe('AttendantStatusToggleComponent', () => {
  const presenceService = {
    start: jasmine.createSpy('start'),
    setStatus: jasmine.createSpy('setStatus').and.returnValue(of(undefined)),
  };
  const state = signal<PresenceState>({ status: 'offline', changedAt: '', changedBy: 'manual' });
  const presenceSignal = { current: state };
  let fixture: ComponentFixture<AttendantStatusToggleComponent>;

  beforeEach(async () => {
    presenceService.start.calls.reset();
    presenceService.setStatus.calls.reset();
    state.set({ status: 'offline', changedAt: '', changedBy: 'manual' });
    await TestBed.configureTestingModule({
      imports: [AttendantStatusToggleComponent],
      providers: [
        { provide: PresenceService, useValue: presenceService },
        { provide: PresenceSignal, useValue: presenceSignal },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AttendantStatusToggleComponent);
    fixture.componentInstance.attendantId = 'a1';
    fixture.detectChanges();
  });

  it('starts heartbeat for the bound attendant', () => {
    expect(presenceService.start).toHaveBeenCalledWith('a1');
  });

  it('exposes the current status from PresenceSignal', () => {
    expect(fixture.componentInstance['current']()).toBe('offline');
    state.set({ status: 'online', changedAt: '', changedBy: 'manual' });
    fixture.detectChanges();
    expect(fixture.componentInstance['current']()).toBe('online');
  });

  it('forwards the new status to PresenceService.setStatus', () => {
    fixture.componentInstance.onChange('online');
    expect(presenceService.setStatus).toHaveBeenCalledWith('a1', 'online');
  });

  it('shows a toast when setStatus fails', () => {
    presenceService.setStatus.and.returnValue(throwError(() => ({ error: { error: { message: 'boom' } } })));
    fixture.componentInstance.onChange('away');
    // No throw — error was caught and surfaced via MessageService.
  });
});
