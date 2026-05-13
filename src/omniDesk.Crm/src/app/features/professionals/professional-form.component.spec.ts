import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { ProfessionalFormComponent } from './professional-form.component';
import { ProfessionalsService } from './professionals.service';

const stubRoute = { snapshot: { paramMap: { get: () => 'novo' } } };

describe('ProfessionalFormComponent (new)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProfessionalFormComponent, RouterTestingModule],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: stubRoute },
        { provide: ProfessionalsService, useValue: {
          list: () => Promise.resolve({ items: [], total: 0 }),
          create: () => Promise.resolve({ id: 'p1', name: 'Test', specialty: null, is_active: true, created_at: '', updated_at: '' }),
        }},
      ],
    }).compileComponents();
  });

  it('initialises in new mode', async () => {
    const fixture = TestBed.createComponent(ProfessionalFormComponent);
    await fixture.whenStable();
    expect(fixture.componentInstance.isNew()).toBeTrue();
  });

  it('form is invalid when name is empty', async () => {
    const fixture = TestBed.createComponent(ProfessionalFormComponent);
    await fixture.whenStable();
    expect(fixture.componentInstance.form.invalid).toBeTrue();
  });
});
