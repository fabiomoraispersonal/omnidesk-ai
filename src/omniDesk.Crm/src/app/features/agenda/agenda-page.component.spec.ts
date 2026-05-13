import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AgendaPageComponent } from './agenda-page.component';
import { RouterTestingModule } from '@angular/router/testing';

describe('AgendaPageComponent', () => {
  let fixture: ComponentFixture<AgendaPageComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AgendaPageComponent, RouterTestingModule],
    }).compileComponents();
    fixture = TestBed.createComponent(AgendaPageComponent);
    fixture.detectChanges();
  });

  it('renders agenda page header', () => {
    const h1 = fixture.nativeElement.querySelector('h1');
    expect(h1?.textContent?.trim()).toBe('Agenda');
  });

  it('renders three nav tabs', () => {
    const links = fixture.nativeElement.querySelectorAll('.agenda-page__tabs a');
    expect(links.length).toBe(3);
  });
});
