import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { ServicesListComponent } from './services-list.component';
import { ServicesCatalogService } from './services.service';

const mockList = () => Promise.resolve({ items: [
  { id: '1', name: 'Consulta', description: null, category: 'Consulta', duration_minutes: 45,
    price: 200, requires_confirmation: false, is_active: true, created_at: '', updated_at: '' }
], total: 1 });

describe('ServicesListComponent', () => {
  let component: ServicesListComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ServicesListComponent, RouterTestingModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ServicesCatalogService, useValue: { list: mockList, toggle: () => Promise.resolve({}) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ServicesListComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('loads services on init', () => {
    expect(component.items().length).toBe(1);
    expect(component.total()).toBe(1);
  });

  it('formatPrice returns BRL for number', () => {
    const result = component.formatPrice(200);
    expect(result).toContain('200');
  });

  it('formatPrice returns "A combinar" for null', () => {
    expect(component.formatPrice(null)).toBe('A combinar');
  });
});
