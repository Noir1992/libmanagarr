import { TestBed } from '@angular/core/testing';
import { SearchClearWidget } from './search-clear.widget';

describe('SearchClearWidget', () => {
  it('centers the clear glyph in the Material hover area on both axes', async () => {
    await TestBed.configureTestingModule({ imports: [SearchClearWidget] }).compileComponents();
    const fixture = TestBed.createComponent(SearchClearWidget);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    const icon = button.querySelector('.clear-search-icon') as HTMLElement;
    const hoverArea = button.getBoundingClientRect();
    const glyph = icon.getBoundingClientRect();
    expect(hoverArea.width).toBe(28);
    expect(glyph.width).toBe(14);
    expect(Math.abs(glyph.left + glyph.width / 2 - (hoverArea.left + hoverArea.width / 2))).toBeLessThan(1);
    expect(Math.abs(glyph.top + glyph.height / 2 - (hoverArea.top + hoverArea.height / 2))).toBeLessThan(1);
  });
});
