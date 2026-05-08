import { DestroyRef, Injectable, OnDestroy, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, fromEvent, interval, merge } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AttendanceStatus, PresenceSignal } from './presence.signal';

@Injectable({ providedIn: 'root' })
export class PresenceService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly signal = inject(PresenceSignal);
  private readonly destroyRef = inject(DestroyRef);

  private heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  private interactionInLastWindow = false;
  private currentAttendantId: string | null = null;

  /// Activates heartbeat polling for the given attendant. The browser dispatches one heartbeat
  /// every 60s only if the user has interacted with the page during the last window AND the tab
  /// is visible (FR-008/FR-010 — heartbeat reflects real activity).
  start(attendantId: string): void {
    this.currentAttendantId = attendantId;
    this.bindInteractionListeners();
    this.heartbeatTimer = setInterval(() => this.maybeSendHeartbeat(), 60_000);
  }

  setStatus(attendantId: string, status: AttendanceStatus): Observable<void> {
    return new Observable(observer => {
      this.http.patch<{ data: { status: AttendanceStatus; changed_at: string; changed_by: 'manual' | 'system' } }>(
        `${environment.apiUrl}/api/attendants/${attendantId}/status`,
        { status },
      ).subscribe({
        next: resp => {
          this.signal.current.set({
            status: resp.data.status,
            changedAt: resp.data.changed_at,
            changedBy: resp.data.changed_by,
          });
          observer.next();
          observer.complete();
        },
        error: err => observer.error(err),
      });
    });
  }

  ngOnDestroy(): void {
    if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
  }

  private bindInteractionListeners(): void {
    const events: Observable<unknown>[] = [
      fromEvent(document, 'mousemove'),
      fromEvent(document, 'keydown'),
      fromEvent(document, 'click'),
    ];
    merge(...events)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => { this.interactionInLastWindow = true; });

    fromEvent(document, 'visibilitychange')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!document.hidden) this.maybeSendHeartbeat();
      });
  }

  private maybeSendHeartbeat(): void {
    if (!this.currentAttendantId || document.hidden || !this.interactionInLastWindow) return;
    this.interactionInLastWindow = false;
    this.http.patch(`${environment.apiUrl}/api/attendants/${this.currentAttendantId}/heartbeat`, {}).subscribe({
      error: () => undefined,
    });
  }
}
