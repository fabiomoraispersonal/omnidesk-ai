import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { of } from 'rxjs';
import { ServiceFormComponent } from './service-form.component';
import { ServicesCatalogService } from './services.service';
import { MessageService } from 'primeng/api';

const mockSvc = {
  list: () => Promise.resolve({ items: [], total: 0 }),
  create: () => Promise.resolve({} as any),
  update: () => Promise.resolve({} as any),
};

describe('ServiceFormComponent', () => {
  let component: ServiceFormComponent;

  const setupComponent = (paramId?: string) => {
    TestBed.configureTestingModule({
      imports: [ServiceFormComponent, RouterModule.forRoot([])],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        MessageService,
        { provide: ServicesCatalogService, useValue: mockSvc },
        { provide: ActivatedRoute, useValue: {
          snapshot: { paramMap: { get: () => paramId ?? null } },
        }},
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ServiceFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    return fixture;
  };

  it('is in "new" mode when no id param', async () => {
    setupComponent(undefined);
    expect(component.isNew()).toBeTrue();
  });

  it('is in "edit" mode when id param is a UUID', async () => {
    setupComponent('some-uuid');
    expect(component.isNew()).toBeFalse();
    expect(component.serviceId).toBe('some-uuid');
  });

  it('form is invalid when name is empty', () => {
    setupComponent();
    component.form.patchValue({ name: '' });
    expect(component.form.controls.name.invalid).toBeTrue();
  });

  it('form is invalid when duration_minutes is 0', () => {
    setupComponent();
    component.form.patchValue({ name: 'Test', duration_minutes: 0 });
    expect(component.form.controls.duration_minutes.invalid).toBeTrue();
  });

  it('form is valid with name and positive duration', () => {
    setupComponent();
    component.form.patchValue({ name: 'Test', duration_minutes: 30 });
    expect(component.form.valid).toBeTrue();
  });
});
