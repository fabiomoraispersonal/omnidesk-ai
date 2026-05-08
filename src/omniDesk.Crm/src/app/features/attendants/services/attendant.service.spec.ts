import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AttendantService } from './attendant.service';
import { environment } from '../../../../environments/environment';

describe('AttendantService', () => {
  let svc: AttendantService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/attendants`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(AttendantService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() unwraps data', () => {
    svc.list().subscribe(d => expect(d.length).toBe(1));
    http.expectOne(base).flush({ data: [{ id: 'a1', name: 'Maria' }] });
  });

  it('uploadAvatar() POSTs multipart', () => {
    const file = new File(['x'], 'avatar.png', { type: 'image/png' });
    svc.uploadAvatar('a1', file).subscribe();
    const req = http.expectOne(`${base}/a1/avatar`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    req.flush({ data: { avatarUrl: 'https://...' } });
  });

  it('updateDepartments() PUTs payload', () => {
    svc.updateDepartments('a1', { departmentIds: ['d1', 'd2'], primaryDepartmentId: 'd1' }).subscribe();
    const req = http.expectOne(`${base}/a1/departments`);
    expect(req.request.method).toBe('PUT');
    req.flush({ data: { id: 'a1' } });
  });
});
