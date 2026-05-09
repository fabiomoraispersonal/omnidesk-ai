import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CannedResponseFormComponent } from './canned-response-form.component';
import { CannedResponseService } from '../services/canned-response.service';
import { DepartmentService } from '../../departments/services/department.service';

describe('CannedResponseFormComponent', () => {
  const cannedService = {
    create: jasmine.createSpy('create').and.returnValue(of({ id: 'cr1' })),
    update: jasmine.createSpy('update'),
    list: jasmine.createSpy('list'),
  };
  const departmentService = {
    list: jasmine.createSpy('list').and.returnValue(of([{ id: 'd1', name: 'Comercial' }])),
  };
  const route = { snapshot: { paramMap: convertToParamMap({}) } };
  let fixture: ComponentFixture<CannedResponseFormComponent>;

  beforeEach(async () => {
    cannedService.create.calls.reset();
    await TestBed.configureTestingModule({
      imports: [CannedResponseFormComponent],
      providers: [
        provideRouter([]),
        { provide: CannedResponseService, useValue: cannedService },
        { provide: DepartmentService, useValue: departmentService },
        { provide: ActivatedRoute, useValue: route },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(CannedResponseFormComponent);
    fixture.detectChanges();
  });

  it('refuses save with empty fields', () => {
    fixture.componentInstance.save();
    expect(cannedService.create).not.toHaveBeenCalled();
  });

  it('detects variables embedded in content', () => {
    const cmp = fixture.componentInstance as any;
    cmp.form.patchValue({ content: 'Olá {{client_name}}, sou {{attendant_name}}.' });
    expect(cmp.previewVariables()).toEqual(['client_name', 'attendant_name']);
  });

  it('submits canonical payload with null department for global scope', () => {
    const cmp = fixture.componentInstance as any;
    cmp.form.patchValue({ title: 'Saudação', content: 'Olá {{client_name}}' });
    cmp.save();
    expect(cannedService.create).toHaveBeenCalled();
    const payload = cannedService.create.calls.mostRecent().args[0];
    expect(payload.departmentId).toBeNull();
  });
});
