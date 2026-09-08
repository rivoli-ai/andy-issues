import { AfterViewInit, Directive, ElementRef, EventEmitter, HostBinding, HostListener, OnDestroy, Output, inject } from '@angular/core';

/** Focus and background isolation for conditionally rendered editor dialogs. */
@Directive({ selector: '[appDialog]', standalone: true })
export class DialogDirective implements AfterViewInit, OnDestroy {
  private readonly element: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly previous = document.activeElement as HTMLElement | null;
  private readonly isolated: Array<{ element: HTMLElement; inert: boolean }> = [];
  @HostBinding('attr.role') readonly role = 'dialog';
  @HostBinding('attr.aria-modal') readonly modal = 'true';
  @HostBinding('attr.tabindex') readonly tabindex = '-1';
  @Output() dialogDismiss = new EventEmitter<void>();

  ngAfterViewInit(): void {
    let current: HTMLElement | null = this.element.nativeElement;
    while (current?.parentElement && current.parentElement !== document.documentElement) {
      for (const sibling of Array.from(current.parentElement.children)) {
        if (sibling !== current && sibling instanceof HTMLElement) {
          this.isolated.push({ element:sibling, inert:sibling.inert }); sibling.inert = true;
        }
      }
      current = current.parentElement;
    }
    this.controls()[0]?.focus();
  }
  private controls(): HTMLElement[] {
    return Array.from(this.element.nativeElement.querySelectorAll<HTMLElement>('input:not([disabled]),textarea:not([disabled]),select:not([disabled]),button:not([disabled]),a[href],[tabindex="0"]')).filter(node=>node.getClientRects().length > 0);
  }
  @HostListener('keydown', ['$event']) onKeydown(event: KeyboardEvent): void {
    if(event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); this.dialogDismiss.emit(); }
    if(event.key !== 'Tab') return;
    const nodes=this.controls(), first=nodes[0], last=nodes[nodes.length-1];
    if(!first) { event.preventDefault(); this.element.nativeElement.focus(); }
    else if(event.shiftKey && document.activeElement===first) { event.preventDefault(); last.focus(); }
    else if(!event.shiftKey && document.activeElement===last) { event.preventDefault(); first.focus(); }
  }
  ngOnDestroy(): void {
    for(const record of this.isolated) record.element.inert=record.inert;
    if(this.previous?.isConnected) this.previous.focus();
  }
}
