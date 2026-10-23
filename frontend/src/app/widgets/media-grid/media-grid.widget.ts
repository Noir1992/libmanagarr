import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  input,
  InputSignal,
  NgZone,
  OnChanges,
  signal,
  SimpleChanges,
  Signal,
  viewChild,
  WritableSignal,
} from '@angular/core';
import { MediaGridItem } from '../../interfaces/general/media-grid-item';
import { MediaCardWidget } from '../media-card/media-card.widget';
import { computeGridWindow, countGridTemplateColumns, GridWindowRange } from './media-grid.util';

const OVERSCAN_ROWS: number = 2;
const BOOTSTRAP_ITEM_LIMIT: number = 64;

@Component({
  selector: 'app-media-grid',
  imports: [MediaCardWidget],
  templateUrl: './media-grid.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './media-grid.widget.scss',
})
export class MediaGridWidget implements OnChanges {
  readonly items: InputSignal<readonly MediaGridItem[]> = input.required<readonly MediaGridItem[]>();

  private readonly injector: Injector = inject(Injector);
  private readonly ngZone: NgZone = inject(NgZone);
  private readonly destroyRef: DestroyRef = inject(DestroyRef);
  private readonly hostElement: HTMLElement = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  private readonly gridWindowElement: Signal<ElementRef<HTMLDivElement> | undefined> = viewChild<ElementRef<HTMLDivElement>>('gridWindow');
  private readonly gridElement: Signal<ElementRef<HTMLDivElement> | undefined> = viewChild<ElementRef<HTMLDivElement>>('grid');

  private readonly columnCount: WritableSignal<number> = signal(0);
  private readonly cardHeight: WritableSignal<number> = signal(0);
  private readonly rowGap: WritableSignal<number> = signal(0);
  private readonly paddingBottom: WritableSignal<number> = signal(0);
  private readonly measured: WritableSignal<boolean> = signal(false);
  private readonly windowRange: WritableSignal<GridWindowRange | null> = signal(null);

  private scrollTarget: Element | null = null;
  private scrollFrame: number | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private lastObservedWidth: number = -1;

  protected readonly isWindowed: Signal<boolean> = computed((): boolean => this.windowRange() !== null);

  private readonly rowHeight: Signal<number> = computed((): number => (this.cardHeight() > 0 ? this.cardHeight() + this.rowGap() : 0));

  protected readonly visibleItems: Signal<readonly MediaGridItem[]> = computed((): readonly MediaGridItem[] => {
    const items: readonly MediaGridItem[] = this.items();
    const range: GridWindowRange | null = this.windowRange();
    const columns: number = this.columnCount();

    if (range !== null && columns > 0) {
      const start: number = range.firstRow * columns;
      const end: number = Math.min(items.length, (range.lastRow + 1) * columns);
      return items.slice(start, end);
    }

    return this.measured() ? items : items.slice(0, BOOTSTRAP_ITEM_LIMIT);
  });

  protected readonly windowHeight: Signal<number | null> = computed((): number | null => {
    const columns: number = this.columnCount();
    const rowHeight: number = this.rowHeight();

    if (this.windowRange() === null || columns <= 0 || rowHeight <= 0) {
      return null;
    }

    const totalRows: number = Math.ceil(this.items().length / columns);
    return Math.max(0, totalRows * rowHeight - this.rowGap() + this.paddingBottom());
  });

  protected readonly gridTransform: Signal<string | null> = computed((): string | null => {
    const range: GridWindowRange | null = this.windowRange();
    return range === null ? null : `translateY(${range.firstRow * this.rowHeight()}px)`;
  });

  constructor() {
    afterNextRender(
      {
        write: (): void => {
          this.observeResize();
          this.refresh();
        },
      },
      { injector: this.injector },
    );

    this.destroyRef.onDestroy((): void => {
      this.detachScrollListener();

      if (this.scrollFrame !== null) {
        cancelAnimationFrame(this.scrollFrame);
        this.scrollFrame = null;
      }

      this.resizeObserver?.disconnect();
      this.resizeObserver = null;
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['items'] !== undefined) {
      afterNextRender(
        {
          write: (): void => this.refresh(),
        },
        { injector: this.injector },
      );
    }
  }

  private readonly handleScroll = (): void => {
    if (this.scrollFrame !== null) {
      return;
    }

    this.scrollFrame = requestAnimationFrame((): void => {
      this.scrollFrame = null;
      this.ngZone.run((): void => this.refresh());
    });
  };

  private observeResize(): void {
    const target: HTMLElement | undefined = this.gridWindowElement()?.nativeElement;

    if (target === undefined || this.resizeObserver !== null || typeof ResizeObserver === 'undefined') {
      return;
    }

    this.resizeObserver = new ResizeObserver((entries: ResizeObserverEntry[]): void => {
      const width: number = Math.round(entries.at(-1)?.contentRect.width ?? -1);

      if (width === this.lastObservedWidth) {
        return;
      }

      this.lastObservedWidth = width;
      this.ngZone.run((): void => this.refresh());
    });

    this.resizeObserver.observe(target);
    this.lastObservedWidth = Math.round(target.getBoundingClientRect().width);
  }

  private refresh(): void {
    const windowElement: HTMLElement | undefined = this.gridWindowElement()?.nativeElement;
    const gridElement: HTMLElement | undefined = this.gridElement()?.nativeElement;

    if (windowElement === undefined || gridElement === undefined) {
      return;
    }

    const gridStyle: CSSStyleDeclaration = getComputedStyle(gridElement);
    const columns: number = countGridTemplateColumns(gridStyle.gridTemplateColumns);
    const cardElement: HTMLElement | null = gridElement.querySelector<HTMLElement>('.media-card');
    const cardHeight: number = cardElement === null ? 0 : cardElement.getBoundingClientRect().height;

    if (columns <= 0 || cardHeight <= 0) {
      return;
    }

    const rowGap: number = Math.max(0, Number.parseFloat(gridStyle.rowGap) || 0);
    const scroller: Element = this.ensureScrollListener();
    const scrollTop: number = scroller.scrollTop;
    const viewportHeight: number = scroller.clientHeight > 0 ? scroller.clientHeight : globalThis.innerHeight;
    const gridTop: number = windowElement.getBoundingClientRect().top - scroller.getBoundingClientRect().top + scrollTop;

    const range: GridWindowRange | null = computeGridWindow({
      itemCount: this.items().length,
      columns,
      cardHeight,
      rowGap,
      gridTop,
      scrollTop,
      viewportHeight,
      overscanRows: OVERSCAN_ROWS,
    });

    this.columnCount.set(columns);
    this.cardHeight.set(cardHeight);
    this.rowGap.set(rowGap);
    this.paddingBottom.set(Math.max(0, Number.parseFloat(gridStyle.paddingBottom) || 0));
    this.measured.set(true);

    const current: GridWindowRange | null = this.windowRange();
    const unchanged: boolean =
      (current === null && range === null) ||
      (current !== null && range !== null && current.firstRow === range.firstRow && current.lastRow === range.lastRow);

    if (!unchanged) {
      this.windowRange.set(range);
    }
  }

  private ensureScrollListener(): Element {
    const target: Element = this.resolveScrollTarget();

    if (this.scrollTarget !== target) {
      this.detachScrollListener();
      this.scrollTarget = target;
      this.ngZone.runOutsideAngular((): void => target.addEventListener('scroll', this.handleScroll, { passive: true }));
    }

    return target;
  }

  private detachScrollListener(): void {
    if (this.scrollTarget === null) {
      return;
    }

    this.scrollTarget.removeEventListener('scroll', this.handleScroll);
    this.scrollTarget = null;
  }

  private resolveScrollTarget(): Element {
    let node: HTMLElement | null = this.hostElement.parentElement;

    while (node !== null) {
      const overflowY: string = getComputedStyle(node).overflowY;

      if ((overflowY === 'auto' || overflowY === 'scroll') && node.scrollHeight > node.clientHeight + 1) {
        return node;
      }

      node = node.parentElement;
    }

    return document.scrollingElement ?? document.documentElement;
  }
}
