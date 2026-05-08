import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CannedResponseService } from './canned-response.service';
import { environment } from '../../../../environments/environment';

describe('CannedResponseService', () => {
  let svc: CannedResponseService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/canned-responses`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(CannedResponseService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list with q filter sends q param', () => {
    svc.list({ q: 'sauda' }).subscribe();
    const req = http.expectOne(r => r.url === base && r.params.get('q') === 'sauda');
    expect(req.request.method).toBe('GET');
    req.flush({ data: [] });
  });

  it('render() returns rendered text and missing list', () => {
    let captured: any = null;
    svc.render('t1', { ticketId: 'tk1' }).subscribe(r => captured = r);
    const req = http.expectOne(`${base}/render`);
    expect(req.request.body).toEqual({ templateId: 't1', context: { ticketId: 'tk1' } });
    req.flush({ data: { rendered: 'olá Maria', missingVariables: [] } });
    expect(captured.rendered).toBe('olá Maria');
  });

  it('delete() DELETEs and returns void', () => {
    let completed = false;
    svc.delete('cr1').subscribe(() => completed = true);
    const req = http.expectOne(`${base}/cr1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    expect(completed).toBeTrue();
  });
});
