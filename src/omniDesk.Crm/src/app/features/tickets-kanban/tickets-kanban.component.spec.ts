import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { signal } from '@angular/core';

import { TicketsKanbanComponent } from './tickets-kanban.component';
import { TicketsService } from './services/tickets.service';
import { CrmWebSocketService } from '../live-chat-inbox/services/crm-websocket.service';

// ---------------------------------------------------------------------------
// Spec 009 US2 — T113
// Minimal TestBed smoke tests for the Kanban board component.
// Full E2E coverage (drag-drop, WS events) deferred to Playwright/Cypress suite.
// ---------------------------------------------------------------------------

describe('TicketsKanbanComponent', () => {
  let fixture: ComponentFixture<TicketsKanbanComponent>;
  let component: TicketsKanbanComponent;

  const ticketsServiceStub = {
    tickets: signal([]),
    loading: signal(false),
    load: jasmine.createSpy('load').and.returnValue(Promise.resolve()),
    applyWsEvent: jasmine.createSpy('applyWsEvent'),
    patchStatus: jasmine.createSpy('patchStatus').and.returnValue(Promise.resolve()),
  };

  const wsServiceStub = {
    connected: signal(false),
    ticketEvents: signal(null),
    connect: jasmine.createSpy('connect'),
    destroy: jasmine.createSpy('destroy'),
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TicketsKanbanComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: TicketsService, useValue: ticketsServiceStub },
        { provide: CrmWebSocketService, useValue: wsServiceStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TicketsKanbanComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('calls tickets.load() on init', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    expect(ticketsServiceStub.load).toHaveBeenCalled();
  });

  it('renders 3 kanban columns', () => {
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const columns = compiled.querySelectorAll('.kanban-column, [class*="kanban-col"]');
    // 3 columns expected (new / in_progress / waiting_client)
    expect(columns.length).toBeGreaterThanOrEqual(3);
  });
});
