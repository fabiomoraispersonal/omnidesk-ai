import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AppointmentsService } from './appointments.service';
import { environment } from '../../../environments/environment';

describe('AppointmentsService', () => {
  let service: AppointmentsService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/appointments`;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AppointmentsService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list returns items and total', async () => {
    const p = service.list();
    http.expectOne(req => req.url === base).flush({
      success: true, data: [], meta: { page: 1, per_page: 20, total: 0 },
    });
    const res = await p;
    expect(res.items).toEqual([]);
    expect(res.total).toBe(0);
  });

  it('confirm sends PATCH to confirm endpoint', async () => {
    const p = service.confirm('appt-1');
    http.expectOne(`${base}/appt-1/confirm`).flush({ success: true, data: { id: 'appt-1', status: 'confirmed' } });
    await p;
  });

  it('cancel sends PATCH to cancel endpoint', async () => {
    const p = service.cancel('appt-1', 'changed mind');
    const req = http.expectOne(`${base}/appt-1/cancel`);
    expect(req.request.body).toEqual({ cancellation_reason: 'changed mind' });
    req.flush({ success: true, data: { id: 'appt-1', status: 'cancelled' } });
    await p;
  });
});
