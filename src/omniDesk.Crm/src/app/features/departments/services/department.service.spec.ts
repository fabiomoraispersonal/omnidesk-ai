import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DepartmentService } from './department.service';
import { environment } from '../../../../environments/environment';

describe('DepartmentService', () => {
  let svc: DepartmentService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/departments`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(DepartmentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() unwraps data envelope', () => {
    svc.list().subscribe(d => expect(d.length).toBe(1));
    http.expectOne(`${base}?include_inactive=false`).flush({ data: [{ id: 'd1', name: 'Comercial' }] });
  });

  it('create() POSTs and returns created dept', () => {
    svc.create({ name: 'Suporte' }).subscribe(d => expect(d.name).toBe('Suporte'));
    const req = http.expectOne(base);
    expect(req.request.method).toBe('POST');
    req.flush({ data: { id: 'd2', name: 'Suporte' } });
  });

  it('deactivate() DELETEs', () => {
    svc.deactivate('d1').subscribe(() => undefined);
    const req = http.expectOne(`${base}/d1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('surfaces 422 errors to the observable error channel', () => {
    let captured: any = null;
    svc.create({ name: '' }).subscribe({ error: e => captured = e });
    http.expectOne(base).flush(
      { error: { code: 'VALIDATION_FAILED' } },
      { status: 422, statusText: 'Unprocessable Entity' });
    expect(captured.status).toBe(422);
  });
});
