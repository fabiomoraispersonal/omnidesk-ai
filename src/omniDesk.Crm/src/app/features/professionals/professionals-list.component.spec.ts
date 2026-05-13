import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { ProfessionalsListComponent } from './professionals-list.component';
import { ProfessionalsService } from './professionals.service';

describe('ProfessionalsListComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProfessionalsListComponent, RouterTestingModule],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ProfessionalsService, useValue: {
          list: () => Promise.resolve({ items: [
            { id: 'p1', name: 'Dra. Ana', specialty: 'Fisio', department_id: null,
              attendant_id: null, is_active: true, created_at: '', updated_at: '' }
          ], total: 1 }),
          toggle: () => Promise.resolve({ id: 'p1', is_active: false }),
        }},
      ],
    }).compileComponents();
  });

  it('loads professionals on init', async () => {
    const fixture = TestBed.createComponent(ProfessionalsListComponent);
    const comp = fixture.componentInstance;
    await fixture.whenStable();
    expect(comp.items().length).toBe(1);
    expect(comp.items()[0].name).toBe('Dra. Ana');
  });
});
