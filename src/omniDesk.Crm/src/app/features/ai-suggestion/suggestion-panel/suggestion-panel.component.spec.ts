import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SuggestionPanelComponent } from './suggestion-panel.component';
import { SuggestionService } from '../services/suggestion.service';

describe('SuggestionPanelComponent', () => {
  const service = {
    request: jasmine.createSpy('request').and.returnValue(of({
      suggestionId: 'sug1', text: 'olá maria', model: 'gpt-4o',
      elapsedMs: 100, inputTokens: 10, outputTokens: 5,
      contextUsed: { sub_agent_id: null, sub_agent_name: null, messages_used: 3 },
    })),
    recordAction: jasmine.createSpy('recordAction').and.returnValue(of(undefined)),
  };
  let fixture: ComponentFixture<SuggestionPanelComponent>;
  let approvedEvents: { text: string; suggestionId: string; edited: boolean }[];

  beforeEach(async () => {
    service.request.calls.reset();
    service.recordAction.calls.reset();
    approvedEvents = [];
    await TestBed.configureTestingModule({
      imports: [SuggestionPanelComponent],
      providers: [{ provide: SuggestionService, useValue: service }],
    }).compileComponents();
    fixture = TestBed.createComponent(SuggestionPanelComponent);
    fixture.componentInstance.conversationId = 'conv1';
    fixture.componentInstance.approved.subscribe(e => approvedEvents.push(e));
    fixture.detectChanges();
  });

  it('does not auto-fetch — requires explicit click', () => {
    expect(service.request).not.toHaveBeenCalled();
  });

  it('shows preview on success and never sends without approval', () => {
    fixture.componentInstance.request();
    expect(service.request).toHaveBeenCalledWith('conv1', undefined);
    expect(service.recordAction).not.toHaveBeenCalled();
    expect(approvedEvents.length).toBe(0);
  });

  it('emits approved when user clicks send (approved action)', () => {
    fixture.componentInstance.request();
    fixture.componentInstance.send();
    expect(approvedEvents[0].text).toBe('olá maria');
    expect(approvedEvents[0].edited).toBeFalse();
    expect(service.recordAction).toHaveBeenCalledWith('conv1', 'sug1', 'approved', undefined);
  });

  it('records edited when user typed before sending', () => {
    fixture.componentInstance.request();
    fixture.componentInstance.onEdit();
    (fixture.componentInstance as any).editableText = 'olá maria, tudo bem?';
    fixture.componentInstance.send();
    expect(approvedEvents[0].edited).toBeTrue();
    expect(service.recordAction).toHaveBeenCalledWith('conv1', 'sug1', 'edited', 'olá maria, tudo bem?');
  });

  it('records discarded without emitting approved', () => {
    fixture.componentInstance.request();
    fixture.componentInstance.discard();
    expect(approvedEvents.length).toBe(0);
    expect(service.recordAction).toHaveBeenCalledWith('conv1', 'sug1', 'discarded', undefined);
  });

  it('shows error toast when provider fails', () => {
    service.request.and.returnValue(throwError(() => ({
      error: { error: { code: 'AI_PROVIDER_TIMEOUT', message: 'timeout' } },
    })));
    fixture.componentInstance.request();
    // Toast emitted via MessageService — we just confirm no preview was set.
    expect((fixture.componentInstance as any).suggestion()).toBeNull();
  });
});
