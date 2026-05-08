import { Injectable, signal } from '@angular/core';

export type AttendanceStatus = 'online' | 'away' | 'offline';

export interface PresenceState {
  status: AttendanceStatus;
  changedAt: string;
  changedBy: 'manual' | 'system';
}

@Injectable({ providedIn: 'root' })
export class PresenceSignal {
  // Local state of the current attendant only.
  // The supervisor view subscribes to status_changed events directly via PresenceWebsocketService.
  readonly current = signal<PresenceState>({
    status: 'offline',
    changedAt: new Date().toISOString(),
    changedBy: 'manual',
  });
}
