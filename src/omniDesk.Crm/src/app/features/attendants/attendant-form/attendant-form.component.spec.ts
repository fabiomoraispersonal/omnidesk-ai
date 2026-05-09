import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AttendantFormComponent } from './attendant-form.component';
import { AttendantService } from '../services/attendant.service';
import { DepartmentService } from '../../departments/services/department.service';

describe('AttendantFormComponent', () => {
  const attendantService = {
    create: jasmine.createSpy('create').and.returnValue(of({ id: 'a1' })),
    update: jasmine.createSpy('update'),
    updateDepartments: jasmine.createSpy('updateDepartments'),
    uploadAvatar: jasmine.createSpy('uploadAvatar'),
    get: jasmine.createSpy('get'),
  };
  const departmentService = {
    list: jasmine.createSpy('list').and.returnValue(of([
      { id: 'd1', name: 'Comercial' },
      { id: 'd2', name: 'Suporte' },
    ])),
  };
  const route = { snapshot: { paramMap: convertToParamMap({}) } };
  let fixture: ComponentFixture<AttendantFormComponent>;

  beforeEach(async () => {
    attendantService.create.calls.reset();
    await TestBed.configureTestingModule({
      imports: [AttendantFormComponent],
      providers: [
        provideRouter([]),
        { provide: AttendantService, useValue: attendantService },
        { provide: DepartmentService, useValue: departmentService },
        { provide: ActivatedRoute, useValue: route },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AttendantFormComponent);
    fixture.detectChanges();
  });

  it('refuses save when required fields missing', () => {
    const cmp = fixture.componentInstance as any;
    cmp.save();
    expect(attendantService.create).not.toHaveBeenCalled();
  });

  it('rejects primary outside selected departments with explanatory error', () => {
    const cmp = fixture.componentInstance as any;
    cmp.form.patchValue({
      userId: '11111111-1111-1111-1111-111111111111',
      name: 'Maria',
      departmentIds: ['d1'],
      primaryDepartmentId: 'dX',
    });
    cmp.save();
    expect(attendantService.create).not.toHaveBeenCalled();
    expect(cmp.errorMessage()).toContain('principal');
  });

  it('creates attendant with proper payload', () => {
    const cmp = fixture.componentInstance as any;
    cmp.form.patchValue({
      userId: '11111111-1111-1111-1111-111111111111',
      name: 'Maria',
      maxSimultaneousChats: 5,
      departmentIds: ['d1'],
      primaryDepartmentId: 'd1',
    });
    cmp.save();
    expect(attendantService.create).toHaveBeenCalled();
  });
});
