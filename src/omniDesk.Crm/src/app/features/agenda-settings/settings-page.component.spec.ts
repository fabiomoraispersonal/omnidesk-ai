import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AgendaSettingsPageComponent } from './settings-page.component';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

describe('AgendaSettingsPageComponent', () => {
  let fixture: ComponentFixture<AgendaSettingsPageComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AgendaSettingsPageComponent, HttpClientTestingModule, NoopAnimationsModule],
    }).compileComponents();
    fixture = TestBed.createComponent(AgendaSettingsPageComponent);
    fixture.detectChanges();
  });

  it('renders settings page header', () => {
    const h1 = fixture.nativeElement.querySelector('h1');
    expect(h1?.textContent?.trim()).toBe('Configurações da Agenda');
  });
});
