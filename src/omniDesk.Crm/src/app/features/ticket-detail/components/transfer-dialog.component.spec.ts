import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { TransferDialogComponent } from './transfer-dialog.component';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

// ---------------------------------------------------------------------------
// Spec 009 US4 — T133
// Smoke tests for TransferDialogComponent.
// ---------------------------------------------------------------------------

describe('TransferDialogComponent', () => {
  let fixture: ComponentFixture<TransferDialogComponent>;
  let component: TransferDialogComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TransferDialogComponent, NoopAnimationsModule],
      providers: [provideHttpClient()],
    }).compileComponents();

    fixture = TestBed.createComponent(TransferDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('is hidden by default', () => {
    expect(component.visible()).toBeFalse();
  });

  it('opens when open() is called', () => {
    spyOn<any>(component, 'loadDepts').and.stub();
    spyOn<any>(component, 'loadAttendants').and.stub();
    component.open();
    expect(component.visible()).toBeTrue();
  });

  it('canSubmit is false when no target selected', () => {
    component.selectedDeptId.set(null);
    component.selectedAttendantId.set(null);
    expect(component.canSubmit()).toBeFalse();
  });

  it('canSubmit is true when dept selected and type is department', () => {
    component.selectedTargetType.set('department');
    component.selectedDeptId.set('dept-1');
    expect(component.canSubmit()).toBeTrue();
  });

  it('canSubmit is true when attendant selected and type is attendant', () => {
    component.selectedTargetType.set('attendant');
    component.selectedAttendantId.set('att-1');
    expect(component.canSubmit()).toBeTrue();
  });

  it('emits cancelled on cancel', () => {
    let emitted = false;
    component.cancelled.subscribe(() => (emitted = true));
    component.onCancel();
    expect(emitted).toBeTrue();
    expect(component.visible()).toBeFalse();
  });

  it('emits correct department payload on submit', async () => {
    let payload: any;
    component.confirmed.subscribe((p) => (payload = p));

    component.selectedTargetType.set('department');
    component.selectedDeptId.set('dept-abc');
    component.note.set('Test note');

    await component.onSubmit();

    expect(payload).toEqual(
      jasmine.objectContaining({
        target_type: 'department',
        target_department_id: 'dept-abc',
        note: 'Test note',
      }),
    );
  });

  it('emits correct attendant payload on submit', async () => {
    let payload: any;
    component.confirmed.subscribe((p) => (payload = p));

    component.selectedTargetType.set('attendant');
    component.selectedAttendantId.set('att-xyz');

    await component.onSubmit();

    expect(payload).toEqual(
      jasmine.objectContaining({
        target_type: 'attendant',
        target_attendant_id: 'att-xyz',
      }),
    );
  });
});
