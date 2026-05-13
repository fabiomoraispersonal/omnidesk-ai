import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ScheduleBlocksComponent } from './schedule-blocks.component';
import { ProfessionalsService } from './professionals.service';

const block = { id: 'b1', professional_id: 'p1', start_at: '2026-06-01T08:00:00Z',
                end_at: '2026-06-05T17:00:00Z', reason: 'Férias' };

describe('ScheduleBlocksComponent', () => {
  const stubSvc = {
    listBlocks: () => Promise.resolve([block]),
    createBlock: () => Promise.resolve(block),
    deleteBlock: () => Promise.resolve(),
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ScheduleBlocksComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ProfessionalsService, useValue: stubSvc },
      ],
    }).compileComponents();
  });

  it('loads blocks on init', async () => {
    const fixture = TestBed.createComponent(ScheduleBlocksComponent);
    fixture.componentInstance.professionalId = 'p1';
    await fixture.whenStable();
    expect(fixture.componentInstance.blocks().length).toBe(1);
    expect(fixture.componentInstance.blocks()[0].reason).toBe('Férias');
  });
});
