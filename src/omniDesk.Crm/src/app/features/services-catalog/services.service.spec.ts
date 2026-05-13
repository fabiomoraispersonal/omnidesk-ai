import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ServicesCatalogService } from './services.service';
import { environment } from '../../../environments/environment';

describe('ServicesCatalogService', () => {
  let service: ServicesCatalogService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api/services`;

  const mockService = {
    id: '1111-1111-1111-1111',
    name: 'Consulta',
    description: null,
    category: 'Consulta',
    duration_minutes: 45,
    price: 200.0,
    requires_confirmation: false,
    is_active: true,
    created_at: '2026-05-12T00:00:00Z',
    updated_at: '2026-05-12T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ServicesCatalogService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() calls GET /api/services with defaults', async () => {
    const promise = service.list();
    const req = http.expectOne(r => r.url === base && r.method === 'GET');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('per_page')).toBe('50');
    req.flush({ success: true, data: [mockService], meta: { page: 1, per_page: 50, total: 1 } });
    const { items, total } = await promise;
    expect(items.length).toBe(1);
    expect(total).toBe(1);
  });

  it('list() passes include_inactive when true', async () => {
    const promise = service.list({ includeInactive: true });
    const req = http.expectOne(r => r.url === base);
    expect(req.request.params.get('include_inactive')).toBe('true');
    req.flush({ success: true, data: [], meta: { page: 1, per_page: 50, total: 0 } });
    await promise;
  });

  it('create() calls POST /api/services', async () => {
    const promise = service.create({ name: 'Sessão', duration_minutes: 60, requires_confirmation: false });
    const req = http.expectOne({ url: base, method: 'POST' });
    req.flush({ success: true, data: mockService });
    const result = await promise;
    expect(result.name).toBe('Consulta');
  });

  it('update() calls PUT /api/services/{id}', async () => {
    const promise = service.update('abc', { name: 'Updated', duration_minutes: 30, requires_confirmation: false });
    const req = http.expectOne({ url: `${base}/abc`, method: 'PUT' });
    req.flush({ success: true, data: { ...mockService, name: 'Updated' } });
    const result = await promise;
    expect(result.name).toBe('Updated');
  });

  it('toggle() calls PATCH /api/services/{id}/toggle', async () => {
    const promise = service.toggle('abc', false);
    const req = http.expectOne({ url: `${base}/abc/toggle`, method: 'PATCH' });
    expect(req.request.body).toEqual({ is_active: false });
    req.flush({ success: true, data: { id: 'abc', is_active: false } });
    const result = await promise;
    expect(result.is_active).toBeFalse();
  });
});
