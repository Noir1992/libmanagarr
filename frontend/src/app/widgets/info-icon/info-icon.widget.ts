import { ChangeDetectionStrategy, Component, computed, input, InputSignal, signal, viewChild } from '@angular/core';
import { CdkOverlayOrigin, ConnectedPosition, OverlayModule } from '@angular/cdk/overlay';
import { MatButtonModule } from '@angular/material/button';

const POPOVER_VIEWPORT_MARGIN = 5;
const POPOVER_GAP = 12;
const POPOVER_MAX_WIDTH = 280;
const POPOVER_FALLBACK_HEIGHT = 60;
const POPOVER_ARROW_INSET = 14;

type InfoIconPlacement = 'right' | 'left' | 'below' | 'above';

interface PopoverLayout {
  placement: InfoIconPlacement;
  panelX: number;
  panelY: number;
  arrowOffset: number;
}

@Component({
  selector: 'app-info-icon',
  imports: [MatButtonModule, OverlayModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './info-icon.widget.html',
  styleUrl: './info-icon.widget.scss',
})
export class InfoIconWidget {
  readonly messageText: InputSignal<string> = input.required<string>();
  readonly size: InputSignal<'small' | 'big'> = input<'small' | 'big'>('small');
  protected readonly isOpen = signal(false);
  protected readonly placement = signal<InfoIconPlacement>('right');
  protected readonly arrowOffset = signal(POPOVER_ARROW_INSET);
  private readonly panelOffset = signal({ x: 0, y: 0 });
  private readonly origin = viewChild.required<CdkOverlayOrigin>('origin');
  private readonly measurePanel = viewChild.required<HTMLElement>('measurePanel');

  protected readonly positions = computed<ConnectedPosition[]>(() => [
    {
      originX: 'start',
      originY: 'top',
      overlayX: 'start',
      overlayY: 'top',
      offsetX: this.panelOffset().x,
      offsetY: this.panelOffset().y,
    },
  ]);

  protected readonly arrowTop = computed<number | null>(() =>
    this.placement() === 'right' || this.placement() === 'left' ? this.arrowOffset() : null,
  );

  protected readonly arrowLeft = computed<number | null>(() =>
    this.placement() === 'below' || this.placement() === 'above' ? this.arrowOffset() : null,
  );

  protected toggle(): void {
    if (this.isOpen()) {
      this.close();
      return;
    }
    const iconRect = this.origin().elementRef.nativeElement.getBoundingClientRect();
    const layout = this.computeLayout(
      { left: iconRect.left, right: iconRect.right, top: iconRect.top, bottom: iconRect.bottom },
      this.measurePanelSize(),
      { width: window.innerWidth, height: window.innerHeight },
    );
    this.placement.set(layout.placement);
    this.arrowOffset.set(layout.arrowOffset);
    this.panelOffset.set({ x: layout.panelX - iconRect.left, y: layout.panelY - iconRect.top });
    this.isOpen.set(true);
  }

  protected close(): void {
    this.isOpen.set(false);
  }

  protected onOverlayKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.close();
    }
  }

  /* The offscreen twin of the panel reports its real box size before the overlay opens. */
  private measurePanelSize(): { width: number; height: number } {
    const element = this.measurePanel();
    const width = element.offsetWidth || Math.min(POPOVER_MAX_WIDTH, window.innerWidth - 2 * POPOVER_VIEWPORT_MARGIN);
    const height = element.offsetHeight || POPOVER_FALLBACK_HEIGHT;
    return { width, height };
  }

  /*
   * Sides first, then below/above; the panel box is clamped to POPOVER_VIEWPORT_MARGIN of every
   * viewport edge and the arrow follows the icon centre along the facing panel edge.
   */
  private computeLayout(
    icon: Pick<DOMRect, 'left' | 'right' | 'top' | 'bottom'>,
    panel: { width: number; height: number },
    viewport: { width: number; height: number },
  ): PopoverLayout {
    const iconCenterX = (icon.left + icon.right) / 2;
    const iconCenterY = (icon.top + icon.bottom) / 2;
    const spaceRight = viewport.width - icon.right - POPOVER_VIEWPORT_MARGIN;
    const spaceLeft = icon.left - POPOVER_VIEWPORT_MARGIN;
    const sideNeeded = panel.width + POPOVER_GAP;

    if (spaceRight >= sideNeeded || spaceLeft >= sideNeeded) {
      const placement: InfoIconPlacement = spaceRight >= sideNeeded ? 'right' : 'left';
      const panelX = placement === 'right' ? icon.right + POPOVER_GAP : icon.left - POPOVER_GAP - panel.width;
      const panelY = clamp(
        iconCenterY - panel.height / 2,
        POPOVER_VIEWPORT_MARGIN,
        viewport.height - POPOVER_VIEWPORT_MARGIN - panel.height,
      );
      return {
        placement,
        panelX,
        panelY,
        arrowOffset: clamp(iconCenterY - panelY, POPOVER_ARROW_INSET, panel.height - POPOVER_ARROW_INSET),
      };
    }

    const placement: InfoIconPlacement = viewport.height - icon.bottom >= icon.top ? 'below' : 'above';
    const panelX = clamp(iconCenterX - panel.width / 2, POPOVER_VIEWPORT_MARGIN, viewport.width - POPOVER_VIEWPORT_MARGIN - panel.width);
    const panelY = placement === 'below' ? icon.bottom + POPOVER_GAP : icon.top - POPOVER_GAP - panel.height;
    return {
      placement,
      panelX,
      panelY,
      arrowOffset: clamp(iconCenterX - panelX, POPOVER_ARROW_INSET, panel.width - POPOVER_ARROW_INSET),
    };
  }
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}
