import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AvailabilitySlot {
  start_at: string;
  end_at: string;
}

interface AvailabilityEnvelope {
  success: boolean;
  data: AvailabilitySlot[];
  meta: { professional_id: string; service_id: string; date: string; timezone: string };
}

/**
 * Spec 011 US3 (T101) — HTTP client for GET /api/availability.
 * Used by the appointment-form to populate slot picker.
 */
@Injectable({ providedIn: 'root' })
export class AvailabilityService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/availability`;

  async getSlots(professionalId: string, serviceId: string, date: string): Promise<AvailabilitySlot[]> {
    const env = await firstValueFrom(
      this.http.get<AvailabilityEnvelope>(this.base, {
        params: {
          professional_id: professionalId,
          service_id:      serviceId,
          date,
        },
      }),
    );
    return env.data;
  }
}
