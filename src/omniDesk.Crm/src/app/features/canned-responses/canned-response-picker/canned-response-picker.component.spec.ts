import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Component, ViewChild, ElementRef } from '@angular/core';
import { of } from 'rxjs';
import { CannedResponsePickerComponent } from './canned-response-picker.component';
import { CannedResponseService } from '../services/canned-response.service';

@Component({
  standalone: true,
  imports: [CannedResponsePickerComponent],
  template: `
    <textarea #ta></textarea>
    <omni-canned-response-picker
      [input]="{ textareaRef: ta }"
      (inserted)="onInsert($event)">
    </omni-canned-response-picker>
  `,
})
class HostComponent {
  @ViewChild('ta') textarea!: ElementRef<HTMLTextAreaElement>;
  inserted = '';
  onInsert(text: string) { this.inserted = text; }
}

describe('CannedResponsePickerComponent', () => {
  const service = {
    list: jasmine.createSpy('list').and.returnValue(of([
      { id: 'cr1', title: 'Saudação', scope: 'global', departmentId: null, content: 'Olá' },
    ])),
    render: jasmine.createSpy('render').and.returnValue(of({ rendered: 'Olá Maria', missingVariables: [] })),
  };
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    service.list.calls.reset();
    service.render.calls.reset();
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: CannedResponseService, useValue: service }],
    }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('opens picker when user types `/something` at the end of the text', fakeAsync(() => {
    const ta = fixture.componentInstance.textarea.nativeElement;
    ta.value = 'Bom dia, /sauda';
    ta.dispatchEvent(new Event('input', { bubbles: true }));
    tick(200);
    fixture.detectChanges();
    expect(service.list).toHaveBeenCalled();
  }));

  it('replaces /query suffix with the rendered text on selection', fakeAsync(() => {
    const ta = fixture.componentInstance.textarea.nativeElement;
    ta.value = 'Olá, /sauda';
    ta.dispatchEvent(new Event('input', { bubbles: true }));
    tick(200);
    fixture.detectChanges();

    const cmp = fixture.debugElement.children[1].componentInstance as any;
    cmp.onSelect({ id: 'cr1', title: 'Saudação' });
    tick();
    expect(ta.value).toBe('Olá, Olá Maria');
    expect(fixture.componentInstance.inserted).toBe('Olá Maria');
  }));
});
