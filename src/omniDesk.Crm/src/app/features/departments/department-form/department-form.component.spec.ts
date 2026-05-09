import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { DepartmentFormComponent } from './department-form.component';
import { DepartmentService } from '../services/department.service';

describe('DepartmentFormComponent', () => {
  const service = {
    get: jasmine.createSpy('get'),
    create: jasmine.createSpy('create').and.returnValue(of({ id: 'new', name: 'Comercial' })),
    update: jasmine.createSpy('update'),
  };
  const route = { snapshot: { paramMap: convertToParamMap({}) } };
  let fixture: ComponentFixture<DepartmentFormComponent>;

  beforeEach(async () => {
    service.create.calls.reset();
    await TestBed.configureTestingModule({
      imports: [DepartmentFormComponent],
      providers: [
        provideRouter([]),
        { provide: DepartmentService, useValue: service },
        { provide: ActivatedRoute, useValue: route },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DepartmentFormComponent);
    fixture.detectChanges();
  });

  it('blocks submit while name is empty', () => {
    const cmp = fixture.componentInstance as any;
    cmp.save();
    expect(service.create).not.toHaveBeenCalled();
  });

  it('submits with the canonical payload shape when valid', () => {
    const cmp = fixture.componentInstance as any;
    cmp.form.patchValue({ name: 'Comercial' });
    cmp.save();
    expect(service.create).toHaveBeenCalled();
    const call = service.create.calls.mostRecent().args[0];
    expect(call.name).toBe('Comercial');
    expect(call.businessHours).toBeNull();
  });

  it('enables hours object when toggled on', () => {
    const cmp = fixture.componentInstance as any;
    cmp.form.patchValue({ name: 'Suporte', enableBusinessHours: true });
    cmp.save();
    const payload = service.create.calls.mostRecent().args[0];
    expect(payload.businessHours.start).toBeTruthy();
    expect(payload.businessHours.days.length).toBeGreaterThan(0);
  });
});
