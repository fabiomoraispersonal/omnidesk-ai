import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ProfessionalsService } from './professionals.service';
import { environment } from '../../../environments/environment';

describe('ProfessionalsService', () => {
  let service: ProfessionalsService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/professionals`;

  const mockProf = {
    id: 'p1', name: 'Dra. Ana', specialty: 'Fisio', department_id: null,
    attendant_id: null, is_active: true, created_at: '', updated_at: '',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ProfessionalsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() calls GET /api/professionals', async () => {
    const p = service.list();
    const req = http.expectOne(r => r.url === base);
    req.flush({ success: true, data: [mockProf], meta: { total: 1 } });
    const { items } = await p;
    expect(items.length).toBe(1);
  });

  it('create() calls POST /api/professionals', async () => {
    const p = service.create({ name: 'Dr. X' });
    const req = http.expectOne({ url: base, method: 'POST' });
    req.flush({ success: true, data: mockProf });
    const result = await p;
    expect(result.name).toBe('Dra. Ana');
  });

  it('toggle() calls PATCH /api/professionals/{id}/toggle', async () => {
    const p = service.toggle('p1', false);
    const req = http.expectOne({ url: `${base}/p1/toggle`, method: 'PATCH' });
    req.flush({ success: true, data: { id: 'p1', is_active: false } });
    const result = await p;
    expect(result.is_active).toBeFalse();
  });

  it('updateServices() calls PUT /api/professionals/{id}/services', async () => {
    const p = service.updateServices('p1', ['s1', 's2']);
    const req = http.expectOne({ url: `${base}/p1/services`, method: 'PUT' });
    expect(req.request.body).toEqual({ service_ids: ['s1', 's2'] });
    req.flush({ success: true });
    await p;
  });

  it('deleteBlock() calls DELETE /api/professionals/{id}/blocks/{blockId}', async () => {
    const p = service.deleteBlock('p1', 'b1');
    http.expectOne({ url: `${base}/p1/blocks/b1`, method: 'DELETE' }).flush({});
    await p;
  });
});
