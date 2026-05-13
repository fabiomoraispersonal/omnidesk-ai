import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AvailabilityService } from './availability.service';
import { environment } from '../../../environments/environment';

describe('AvailabilityService', () => {
  let service: AvailabilityService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AvailabilityService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getSlots returns slots array', async () => {
    const slot = { start_at: '2026-06-10T09:00:00Z', end_at: '2026-06-10T09:30:00Z' };
    const p = service.getSlots('prof-1', 'svc-1', '2026-06-10');
    http.expectOne(req =>
      req.url === `${environment.apiUrl}/api/availability` &&
      req.params.get('professional_id') === 'prof-1',
    ).flush({ success: true, data: [slot], meta: {} });
    const res = await p;
    expect(res.length).toBe(1);
    expect(res[0].start_at).toBe(slot.start_at);
  });
});
