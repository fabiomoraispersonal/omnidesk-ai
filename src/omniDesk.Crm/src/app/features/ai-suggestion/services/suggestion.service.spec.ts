import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SuggestionService } from './suggestion.service';
import { environment } from '../../../../environments/environment';

describe('SuggestionService', () => {
  let svc: SuggestionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(SuggestionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('request POSTs and unwraps data', () => {
    let captured: any = null;
    svc.request('conv1', 15).subscribe(r => captured = r);
    const req = http.expectOne(`${environment.apiUrl}/api/conversations/conv1/suggest-reply`);
    expect(req.request.body).toEqual({ contextMessageCount: 15 });
    req.flush({ data: { suggestionId: 'sug1', text: 'olá', model: 'gpt-4o' } });
    expect(captured.suggestionId).toBe('sug1');
  });

  it('recordAction PATCHes with humanAction + finalText', () => {
    svc.recordAction('conv1', 'sug1', 'edited', 'final').subscribe();
    const req = http.expectOne(`${environment.apiUrl}/api/conversations/conv1/suggestions/sug1`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ humanAction: 'edited', finalMessageText: 'final' });
    req.flush(null);
  });
});
