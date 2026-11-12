import { TestBed } from '@angular/core/testing';
import { SafeHtml } from '@angular/platform-browser';
import { MarkdownPipe } from './markdown.pipe';

describe('MarkdownPipe', () => {
  let pipe: MarkdownPipe;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [MarkdownPipe] });
    pipe = TestBed.inject(MarkdownPipe);
  });

  // bypassSecurityTrustHtml wraps the string, so the trusted value is read back for assertions.
  function trustedHtml(value: SafeHtml): string {
    return (value as unknown as { changingThisBreaksApplicationSecurity: string }).changingThisBreaksApplicationSecurity;
  }

  it('renders markdown into html elements', () => {
    const html: string = trustedHtml(pipe.transform('**Fixed** the calendar\n- second line'));

    expect(html).toContain('<strong>Fixed</strong>');
    expect(html).toContain('<li>second line</li>');
  });

  it('opens notes links in a new tab without leaking the opener', () => {
    const html: string = trustedHtml(pipe.transform('See [the release](https://github.com/Noir1992/libmanagarr/releases)'));

    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noopener"');
  });

  it('drops script tags and inline event handlers from hostile notes', () => {
    const html: string = trustedHtml(pipe.transform('<img src=x onerror="alert(1)"><script>alert(2)</script>safe'));

    expect(html).not.toContain('<script');
    expect(html).not.toContain('onerror');
    expect(html).toContain('safe');
  });

  it('trusts an empty string for a missing note', () => {
    expect(trustedHtml(pipe.transform(null))).toBe('');
    expect(trustedHtml(pipe.transform(''))).toBe('');
  });
});
