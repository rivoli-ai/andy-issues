import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DialogDirective } from './dialog.directive';

@Component({standalone:true,imports:[DialogDirective],template:`
  <button id="opener" (click)="open=true">Create</button>
  @if(open) { <section appDialog (dialogDismiss)="open=false" aria-label="Create item"><input aria-label="Title"><button>Save</button></section> }
`})
class DialogHarness { open=false; }

describe('Editor dialog accessibility', () => {
  it('isolates the page, contains keyboard focus and restores the trigger on Escape', () => {
    TestBed.configureTestingModule({imports:[DialogHarness]});
    const fixture=TestBed.createComponent(DialogHarness);
    fixture.detectChanges();
    const opener: HTMLButtonElement=fixture.nativeElement.querySelector('#opener');
    opener.focus(); opener.click(); fixture.detectChanges();
    const dialog: HTMLElement=fixture.nativeElement.querySelector('[role="dialog"]');
    const input=dialog.querySelector('input')!;
    const save=dialog.querySelector('button')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(opener.inert).toBeTrue();
    expect(document.activeElement).toBe(input);
    save.focus(); save.dispatchEvent(new KeyboardEvent('keydown',{key:'Tab',bubbles:true,cancelable:true}));
    expect(document.activeElement).toBe(input);
    input.dispatchEvent(new KeyboardEvent('keydown',{key:'Tab',shiftKey:true,bubbles:true,cancelable:true}));
    expect(document.activeElement).toBe(save);
    save.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
    expect(opener.inert).toBeFalse();
    expect(document.activeElement).toBe(opener);
    fixture.destroy();
  });
});
