import { Component, CUSTOM_ELEMENTS_SCHEMA, Input } from '@angular/core';
import { registerMermaidMarkdown } from '@andy-ui/angular';

registerMermaidMarkdown();

/** Angular binding adapter; rendering, sanitization and image controls belong to Andy UI. */
@Component({
  selector: 'app-markdown',
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  template: '<andy-markdown [source]="source || \'\'"></andy-markdown>',
})
export class MarkdownComponent {
  @Input() source: string | null | undefined = '';
}
