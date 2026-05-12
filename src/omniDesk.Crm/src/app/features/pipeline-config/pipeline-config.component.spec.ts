import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute } from '@angular/router';
import { PipelineConfigComponent } from './pipeline-config.component';
import { PipelineConfigService } from './services/pipeline-config.service';

// ---------------------------------------------------------------------------
// Spec 009 US9 — T179
// Smoke tests for PipelineConfigComponent.
// ---------------------------------------------------------------------------

describe('PipelineConfigComponent', () => {
  let fixture: ComponentFixture<PipelineConfigComponent>;
  let component: PipelineConfigComponent;
  let service: jasmine.SpyObj<PipelineConfigService>;

  const mockPipeline = {
    id: 'pipe-1',
    department_id: 'dept-1',
    name: 'Pipeline',
    columns: [
      { id: 'c-1', name: 'Na Fila',      status_mapping: 'new',            order: 1, color: '#6F7D5C' },
      { id: 'c-2', name: 'Em Andamento', status_mapping: 'in_progress',    order: 2, color: '#C09A4D' },
      { id: 'c-3', name: 'Aguardando',   status_mapping: 'waiting_client', order: 3, color: '#B85C5C' },
    ],
    updated_at: '2026-01-01T00:00:00Z',
  };

  beforeEach(async () => {
    service = jasmine.createSpyObj('PipelineConfigService', ['getByDepartment', 'updateColumns'], {
      loading: { set: () => {}, (): boolean { return false; } },
      pipeline: { set: () => {}, (): null { return null; } },
    });
    service.getByDepartment.and.returnValue(Promise.resolve(mockPipeline));
    service.updateColumns.and.returnValue(Promise.resolve({ success: true }));

    await TestBed.configureTestingModule({
      imports: [PipelineConfigComponent, NoopAnimationsModule],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: PipelineConfigService, useValue: service },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'dept-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PipelineConfigComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('calls getByDepartment with departmentId from route', () => {
    expect(service.getByDepartment).toHaveBeenCalledWith('dept-1');
  });

  it('loads columns from pipeline data', async () => {
    await fixture.whenStable();
    // After load, pipelineId should be set
    expect(component.pipelineId()).toBe('pipe-1');
  });

  it('sets 3 columns after load', async () => {
    await fixture.whenStable();
    expect(component.columns().length).toBe(3);
  });

  it('onDrop reorders columns', async () => {
    await fixture.whenStable();
    const before = component.columns().map((c) => c.status_mapping);

    // Move item at index 2 to index 0
    component.onDrop({ previousIndex: 2, currentIndex: 0, item: {} as any, container: {} as any, previousContainer: {} as any, distance: { x: 0, y: 0 }, dropPoint: { x: 0, y: 0 }, isPointerOverContainer: true, event: {} as any });

    const after = component.columns()[0].order;
    expect(after).toBe(1);
  });

  it('calls updateColumns on save', async () => {
    await fixture.whenStable();
    await component.onSave();
    expect(service.updateColumns).toHaveBeenCalled();
  });

  it('shows error message when save fails', async () => {
    service.updateColumns.and.returnValue(Promise.resolve({ success: false, error: 'VALIDATION_ERROR' }));
    await fixture.whenStable();
    await component.onSave();
    expect(component.errorMessage()).toBeTruthy();
  });
});
