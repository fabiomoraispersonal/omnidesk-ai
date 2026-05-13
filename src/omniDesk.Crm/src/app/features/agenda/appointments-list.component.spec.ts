import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppointmentsListComponent } from './appointments-list.component';
import { AppointmentsService } from './appointments.service';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

describe('AppointmentsListComponent', () => {
  let comp: AppointmentsListComponent;
  let fixture: ComponentFixture<AppointmentsListComponent>;
  let svc: jasmine.SpyObj<AppointmentsService>;

  beforeEach(async () => {
    svc = jasmine.createSpyObj('AppointmentsService', ['list']);
    svc.list.and.resolveTo({ items: [], total: 0 });

    await TestBed.configureTestingModule({
      imports: [AppointmentsListComponent, RouterTestingModule, HttpClientTestingModule, NoopAnimationsModule],
      providers: [{ provide: AppointmentsService, useValue: svc }],
    }).compileComponents();

    fixture = TestBed.createComponent(AppointmentsListComponent);
    comp    = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('calls list on init', () => {
    expect(svc.list).toHaveBeenCalled();
  });

  it('statusLabel returns correct label', () => {
    expect(comp.statusLabel('confirmed')).toBe('Confirmado');
    expect(comp.statusLabel('cancelled')).toBe('Cancelado');
  });
});
