import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MediaGridItem } from '../../interfaces/general/media-grid-item';
import { MediaGridWidget } from './media-grid.widget';

@Component({
  imports: [MediaGridWidget],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: ` <app-media-grid [items]="items" /> `,
})
class MediaGridHostComponent {
  items: readonly MediaGridItem[] = [
    {
      kind: 'movie',
      id: 1,
      title: 'Movie 1',
      url: 'http://test/1',
      posterUrl: null,
      detailRows: [],
    },
    {
      kind: 'movie',
      id: 2,
      title: 'Movie 2',
      url: 'http://test/2',
      posterUrl: null,
      detailRows: [],
    },
  ];
}

@Component({
  imports: [MediaGridWidget],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="scroll-host" style="height: 220px; overflow-y: auto">
      <app-media-grid [items]="items" />
    </div>
  `,
})
class ScrollableMediaGridHostComponent {
  readonly items: readonly MediaGridItem[] = Array.from({ length: 200 }, (_: unknown, index: number): MediaGridItem => {
    return { kind: 'movie', id: index, title: `Movie ${index}`, url: `/movie/${index}`, posterUrl: null, detailRows: [] };
  });
}

describe('MediaGridWidget', () => {
  let host: MediaGridHostComponent;
  let fixture: ComponentFixture<MediaGridHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MediaGridHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(MediaGridHostComponent);
    host = fixture.componentInstance;
  });

  it('displays a card for each item', () => {
    host.items = [
      { kind: 'movie', id: 1, title: 'A', url: '/a', posterUrl: null, detailRows: [] },
      { kind: 'movie', id: 2, title: 'B', url: '/b', posterUrl: null, detailRows: [] },
      { kind: 'movie', id: 3, title: 'C', url: '/c', posterUrl: null, detailRows: [] },
    ];
    fixture.detectChanges();
    const cards = fixture.nativeElement.querySelectorAll('app-media-card');
    expect(cards).toHaveSize(3);
  });

  it('renders no cards when items is empty', () => {
    host.items = [];
    fixture.detectChanges();
    const cards = fixture.nativeElement.querySelectorAll('app-media-card');
    expect(cards).toHaveSize(0);
  });

  it('uses grid layout', () => {
    fixture.detectChanges();
    const grid = fixture.nativeElement.querySelector('.media-grid');
    const display = window.getComputedStyle(grid).display;
    expect(display).toBe('grid');
  });
});

describe('MediaGridWidget (windowed rendering)', () => {
  let fixture: ComponentFixture<ScrollableMediaGridHostComponent>;

  async function settle(): Promise<void> {
    await fixture.whenStable();
    await new Promise<void>((resolve: () => void): void => {
      requestAnimationFrame((): void => {
        requestAnimationFrame(resolve);
      });
    });
    await fixture.whenStable();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ScrollableMediaGridHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ScrollableMediaGridHostComponent);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
  });

  it('windows a long grid and renders only a fraction of the cards', () => {
    const wrapper: HTMLElement | null = fixture.nativeElement.querySelector('.media-grid-window');
    const cards: NodeListOf<Element> = fixture.nativeElement.querySelectorAll('app-media-card');

    expect(wrapper?.classList.contains('is-windowed')).toBeTrue();
    expect(wrapper?.style.height).not.toBe('');
    expect(cards.length).toBeGreaterThan(0);
    expect(cards.length).toBeLessThan(64);
  });

  it('renders the scrolled rows after scrolling', async () => {
    const scrollHost: HTMLElement | null = fixture.nativeElement.querySelector('.scroll-host');
    expect(scrollHost).toBeTruthy();

    scrollHost!.scrollTop = 3000;
    scrollHost!.dispatchEvent(new Event('scroll'));
    await settle();
    fixture.detectChanges();

    const cards: NodeListOf<Element> = fixture.nativeElement.querySelectorAll('app-media-card');
    expect(cards.length).toBeGreaterThan(0);
    expect(cards.length).toBeLessThan(64);

    const firstTitle: string | undefined = cards[0].querySelector('.media-card-title')?.textContent?.trim();
    expect(firstTitle).toBeDefined();
    expect(firstTitle).not.toContain('Movie 0');
  });
});
