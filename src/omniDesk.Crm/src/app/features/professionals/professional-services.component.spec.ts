import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ProfessionalServicesComponent } from './professional-services.component';
import { ProfessionalsService } from './professionals.service';
import { ServicesService } from '../services-catalog/services.service';

describe('ProfessionalServicesComponent', () => {
  const stubCatalog = { list: () => Promise.resolve({ items: [
    { id: 's1', name: 'Consulta', duration_minutes: 30, price: 150, is_active: true, created_at: '' }
  ], total: 1 }) };
  const stubProfSvc = {
    getServices: () => Promise.resolve([{ id: 's1', name: 'Consulta' }]),
    updateServices: () => Promise.resolve([]),
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProfessionalServicesComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ServicesService, useValue: stubCatalog },
        { provide: ProfessionalsService, useValue: stubProfSvc },
      ],
    }).compileComponents();
  });

  it('loads and shows linked service as checked', async () => {
    const fixture = TestBed.createComponent(ProfessionalServicesComponent);
    fixture.componentInstance.professionalId = 'p1';
    await fixture.whenStable();
    expect(fixture.componentInstance.allServices().length).toBe(1);
    expect(fixture.componentInstance.isLinked('s1')).toBeTrue();
  });

  it('toggle unlinks a linked service', async () => {
    const fixture = TestBed.createComponent(ProfessionalServicesComponent);
    fixture.componentInstance.professionalId = 'p1';
    await fixture.whenStable();
    fixture.componentInstance.toggle('s1');
    expect(fixture.componentInstance.isLinked('s1')).toBeFalse();
  });
});
