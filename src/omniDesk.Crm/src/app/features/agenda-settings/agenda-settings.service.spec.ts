import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { AgendaSettingsService } from './agenda-settings.service';

describe('AgendaSettingsService', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [HttpClientTestingModule] }));

  it('should be created', () => {
    expect(TestBed.inject(AgendaSettingsService)).toBeTruthy();
  });
});
