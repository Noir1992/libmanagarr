import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  InputSignal,
  input,
  linkedSignal,
  OutputEmitterRef,
  output,
  WritableSignal,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AppI18nService } from '../../services/app-i18n.service';
import { TrendChartLabel, TrendChartModel, TrendChartPoint, TrendChartSeries } from '../../types/trend-chart/trend-chart';
import { TrendChartWidgetTooltipState } from '../../types/trend-chart/trend-chart-widget';

const CHART_WIDTH: number = 960;
const CHART_HEIGHT: number = 360;
const CHART_PADDING: Readonly<{ top: number; right: number; bottom: number; left: number }> = {
  top: 24,
  right: 24,
  bottom: 56,
  left: 72,
};

@Component({
  selector: 'app-trend-chart-widget',
  imports: [MatIconButton, MatIconModule],
  templateUrl: './trend-chart.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './trend-chart.widget.scss',
})
export class TrendChartWidget {
  /* Inputs */
  readonly chartId: InputSignal<TrendChartSeries> = input.required<TrendChartSeries>();
  readonly title: InputSignal<string> = input.required<string>();
  readonly chart: InputSignal<TrendChartModel | null> = input.required<TrendChartModel | null>();
  readonly chartAccentColor: InputSignal<string> = input.required<string>();

  /* Outputs */
  readonly pointSelected: OutputEmitterRef<string> = output<string>();

  private readonly i18n = inject(AppI18nService);
  protected readonly zoomLabels = computed(() => ({
    zoomIn: this.i18n.t('libraryTrend.zoomIn'),
    zoomOut: this.i18n.t('libraryTrend.zoomOut'),
    reset: this.i18n.t('libraryTrend.resetZoom'),
    previous: this.i18n.t('libraryTrend.previousRange'),
    next: this.i18n.t('libraryTrend.nextRange'),
    hint: this.i18n.t('libraryTrend.zoomHint'),
  }));
  protected readonly viewport = linkedSignal({ source: this.chart, computation: () => ({ start: 0, end: 1 }) });
  protected readonly zoomed = computed(() => this.viewport().end - this.viewport().start < 1);
  protected readonly canZoomIn = computed(
    () => (this.chart()?.points.length ?? 0) > 1 && this.viewport().end - this.viewport().start > this.minimumSpan() + 1e-8,
  );
  protected readonly displayChart = computed<TrendChartModel | null>(() => {
    const chart = this.chart();
    if (!chart || !this.zoomed()) return chart;
    const { start, end } = this.viewport();
    const width = CHART_WIDTH - CHART_PADDING.left - CHART_PADDING.right;
    const points = chart.points.map((point) => ({
      ...point,
      x: CHART_PADDING.left + (point.x - CHART_PADDING.left - start * width) / (end - start),
    }));
    const visible = points.filter((point) => point.x >= CHART_PADDING.left && point.x <= CHART_WIDTH - CHART_PADDING.right);
    const first = visible[0];
    const last = visible.at(-1);
    const xLabels: TrendChartLabel[] = [];
    if (first && last) {
      if (first === last) {
        xLabels.push({ x: first.x, label: first.tooltipLines[0], anchor: 'middle' });
      } else {
        xLabels.push(
          { x: first.x, label: first.tooltipLines[0], anchor: 'start' },
          { x: last.x, label: last.tooltipLines[0], anchor: 'end' },
        );
      }
    }
    return {
      ...chart,
      points,
      linePath: points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x} ${point.y}`).join(' '),
      areaPath:
        points.length < 2
          ? null
          : `M ${points[0].x} ${CHART_HEIGHT - CHART_PADDING.bottom} ${points.map((point) => `L ${point.x} ${point.y}`).join(' ')} L ${points.at(-1)!.x} ${CHART_HEIGHT - CHART_PADDING.bottom} Z`,
      xLabels,
    };
  });
  private drag: { pointerId: number; clientX: number; start: number; span: number; width: number } | null = null;
  private dragMoved: boolean = false;

  protected changeZoom(factor: number, anchor = 0.5): void {
    if ((this.chart()?.points.length ?? 0) < 2) return;
    const current = this.viewport();
    const span = current.end - current.start;
    const nextSpan = Math.min(1, Math.max(this.minimumSpan(), span / factor));
    this.setViewport(current.start + anchor * (span - nextSpan), nextSpan);
  }

  protected resetZoom(): void {
    this.setViewport(0, 1);
  }

  protected pan(direction: number): void {
    const { start, end } = this.viewport();
    this.setViewport(start + direction * (end - start) * 0.25, end - start);
  }

  protected onWheel(event: WheelEvent): void {
    const svg = event.currentTarget as SVGSVGElement;
    const rect = svg.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * CHART_WIDTH;
    if (x < CHART_PADDING.left || x > CHART_WIDTH - CHART_PADDING.right || event.deltaY === 0 || (this.chart()?.points.length ?? 0) < 2)
      return;
    event.preventDefault();
    const anchor = (x - CHART_PADDING.left) / (CHART_WIDTH - CHART_PADDING.left - CHART_PADDING.right);
    this.changeZoom(event.deltaY < 0 ? 1.25 : 0.8, anchor);
  }

  protected onPointerDown(event: PointerEvent): void {
    if (!this.zoomed() || event.button !== 0) return;
    const svg = event.currentTarget as SVGSVGElement;
    const { start, end } = this.viewport();
    const width = (svg.getBoundingClientRect().width * (CHART_WIDTH - CHART_PADDING.left - CHART_PADDING.right)) / CHART_WIDTH;
    if (width <= 0) return;
    this.drag = { pointerId: event.pointerId, clientX: event.clientX, start, span: end - start, width };
    this.dragMoved = false;
    svg.setPointerCapture(event.pointerId);
    this.onChartHoverEnd();
  }

  protected onPointerMove(event: PointerEvent): void {
    if (!this.drag || this.drag.pointerId !== event.pointerId) return;
    if (Math.abs(event.clientX - this.drag.clientX) > 3) {
      this.dragMoved = true;
    }
    this.setViewport(this.drag.start - ((event.clientX - this.drag.clientX) / this.drag.width) * this.drag.span, this.drag.span);
  }

  protected onPointerEnd(): void {
    this.drag = null;
  }

  private minimumSpan(): number {
    return 1 / Math.max(2, Math.min(64, (this.chart()?.points.length ?? 0) - 1));
  }

  private setViewport(start: number, span: number): void {
    start = Math.max(0, Math.min(1 - span, start));
    this.viewport.set({ start, end: start + span });
    this.onChartHoverEnd();
  }

  /* Signals */
  protected readonly hoverTooltip: WritableSignal<TrendChartWidgetTooltipState> = linkedSignal<
    TrendChartModel | null,
    TrendChartWidgetTooltipState
  >({ source: this.chart, computation: () => null });
  protected readonly hoverLineTop: number = CHART_PADDING.top;
  protected readonly hoverLineBottom: number = CHART_HEIGHT - CHART_PADDING.bottom;

  /* Handlers */
  protected onChartHoverMove(event: MouseEvent, chart: TrendChartModel): void {
    const svgElement: SVGSVGElement | null = event.currentTarget instanceof SVGSVGElement ? event.currentTarget : null;
    if (svgElement === null) {
      return;
    }

    if (this.drag !== null) return;
    const rect: DOMRect = svgElement.getBoundingClientRect();
    if (rect.width <= 0) return;
    const svgX: number = ((event.clientX - rect.left) / rect.width) * CHART_WIDTH;
    if (svgX < CHART_PADDING.left || svgX > CHART_WIDTH - CHART_PADDING.right) {
      this.onChartHoverEnd();
      return;
    }
    const nearestPoint: TrendChartPoint | null = this.findNearestChartPoint(
      chart.points.filter((point) => point.x >= CHART_PADDING.left && point.x <= CHART_WIDTH - CHART_PADDING.right),
      svgX,
    );
    if (nearestPoint === null) {
      this.onChartHoverEnd();
      return;
    }

    const clientPoint: { x: number; y: number } = this.toClientPoint(rect, nearestPoint);
    this.hoverTooltip.set(this.buildHoverState(clientPoint.x, clientPoint.y, nearestPoint));
  }

  protected onChartHoverEnd(): void {
    this.hoverTooltip.set(null);
  }

  protected onChartPointClick(event: MouseEvent, chart: TrendChartModel): void {
    if (this.dragMoved) {
      this.dragMoved = false;
      return;
    }

    const svgElement: SVGSVGElement | null = event.currentTarget instanceof SVGSVGElement ? event.currentTarget : null;
    if (svgElement === null) {
      return;
    }

    const rect: DOMRect = svgElement.getBoundingClientRect();
    if (rect.width <= 0) return;
    const svgX: number = ((event.clientX - rect.left) / rect.width) * CHART_WIDTH;
    if (svgX < CHART_PADDING.left || svgX > CHART_WIDTH - CHART_PADDING.right) {
      return;
    }
    const nearestPoint: TrendChartPoint | null = this.findNearestChartPoint(
      chart.points.filter((point) => point.x >= CHART_PADDING.left && point.x <= CHART_WIDTH - CHART_PADDING.right),
      svgX,
    );
    if (nearestPoint === null) {
      return;
    }

    this.pointSelected.emit(nearestPoint.occurredAt);
  }

  /* Helpers */
  private buildHoverState(clientX: number, clientY: number, point: TrendChartPoint): TrendChartWidgetTooltipState {
    return {
      chartId: this.chartId(),
      x: clientX,
      y: clientY,
      pointX: point.x,
      pointY: point.y,
      lines: point.tooltipLines,
    };
  }

  private findNearestChartPoint(points: readonly TrendChartPoint[], svgX: number): TrendChartPoint | null {
    if (points.length === 0) {
      return null;
    }

    let nearestPoint: TrendChartPoint = points[0];
    let nearestDistance: number = Math.abs(points[0].x - svgX);

    for (let index: number = 1; index < points.length; index++) {
      const candidate: TrendChartPoint = points[index];
      const distance: number = Math.abs(candidate.x - svgX);
      if (distance < nearestDistance) {
        nearestPoint = candidate;
        nearestDistance = distance;
      }
    }

    return nearestPoint;
  }

  private toClientPoint(rect: DOMRect, point: TrendChartPoint): { x: number; y: number } {
    return {
      x: rect.left + (point.x / CHART_WIDTH) * rect.width,
      y: rect.top + (point.y / CHART_HEIGHT) * rect.height,
    };
  }
}
