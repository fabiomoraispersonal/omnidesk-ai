import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PresenceService } from './presence.service';
import { PresenceSignal } from './presence.signal';
import { environment } from '../../../environments/environment';

describe('PresenceService', () => {
  let svc: PresenceService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), PresenceSignal],
    });
    svc = TestBed.inject(PresenceService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('PATCHes /status and updates the local signal', () => {
    let completed = false;
    svc.setStatus('a1', 'online').subscribe(() => completed = true);
    const req = http.expectOne(`${environment.apiUrl}/api/attendants/a1/status`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'online' });
    req.flush({ data: { status: 'online', changed_at: '2026-05-08T10:00Z', changed_by: 'manual' } });
    expect(completed).toBeTrue();

    const sig = TestBed.inject(PresenceSignal);
    expect(sig.current().status).toBe('online');
  });

  it('does not send heartbeat without interaction', fakeAsync(() => {
    svc.start('a1');
    tick(60_000);
    http.expectNone(`${environment.apiUrl}/api/attendants/a1/heartbeat`);
  }));

  it('sends heartbeat when interaction detected and tab is visible', fakeAsync(() => {
    svc.start('a1');
    spyOnProperty(document, 'hidden').and.returnValue(false);
    document.dispatchEvent(new Event('mousemove'));
    tick(60_000);
    const req = http.expectOne(`${environment.apiUrl}/api/attendants/a1/heartbeat`);
    expect(req.request.method).toBe('PATCH');
    req.flush(null);
  }));
});
