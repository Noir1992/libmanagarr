import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import DOMPurify from 'dompurify';
import { marked } from 'marked';

// Release notes are read next to the release they came from, so a link inside them must never
// replace the page; every surviving anchor is pinned to its own tab.
DOMPurify.addHook('afterSanitizeAttributes', (node: Element): void => {
  if (node.nodeName === 'A' && node.hasAttribute('href')) {
    node.setAttribute('target', '_blank');
    node.setAttribute('rel', 'noopener');
  }
});

@Pipe({
  name: 'markdown',
  pure: true,
})
export class MarkdownPipe implements PipeTransform {
  private readonly sanitizer: DomSanitizer = inject(DomSanitizer);

  transform(value: string | null): SafeHtml {
    if (value === null || value.length === 0) {
      return this.sanitizer.bypassSecurityTrustHtml('');
    }

    // marked renders synchronously with async disabled; DOMPurify strips everything a release
    // note could smuggle in (scripts, event handlers, javascript: urls) before Angular trusts it.
    const html: string = DOMPurify.sanitize(marked.parse(value, { async: false, gfm: true, breaks: true }));

    return this.sanitizer.bypassSecurityTrustHtml(html);
  }
}
