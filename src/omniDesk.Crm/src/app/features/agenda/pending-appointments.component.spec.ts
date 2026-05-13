import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PendingAppointmentsComponent } from './pending-appointments.component';
import { AppointmentsService } from './appointments.service';
import { RouterTestingModule } from '@angular/router/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

describe('PendingAppointmentsComponent', () => {
  let comp: PendingAppointmentsComponent;
  let fixture: ComponentFixture<PendingAppointmentsComponent>;
  let svc: jasmine.SpyObj<AppointmentsService>;

  beforeEach(async () => {
    svc = jasmine.createSpyObj('AppointmentsService', ['list', 'confirm']);
    svc.list.and.resolveTo({ items: [], total: 0 });

    await TestBed.configureTestingModule({
      imports: [PendingAppointmentsComponent, RouterTestingModule, NoopAnimationsModule],
      providers: [{ provide: AppointmentsService, useValue: svc }],
    }).compileComponents();

    fixture = TestBed.createComponent(PendingAppointmentsComponent);
    comp    = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('loads pending appointments on init', () => {
    expect(svc.list).toHaveBeenCalledWith(jasmine.objectContaining({ status: 'pending_confirmation' }));
  });

  it('shows empty message when no pending appointments', () => {
    const el = fixture.nativeElement.querySelector('.pending__empty');
    expect(el).toBeTruthy();
  });
});
