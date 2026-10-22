import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TrendChartWidget } from './trend-chart.widget';

describe('TrendChartWidget', () => {
  let fixture: ComponentFixture<TrendChartWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TrendChartWidget],
    }).compileComponents();

    fixture = TestBed.createComponent(TrendChartWidget);
    fixture.componentRef.setInput('chartId', 'media');
    fixture.componentRef.setInput('title', 'Media changes');
    fixture.componentRef.setInput('chartAccentColor', 'var(--theme-accent)');
    fixture.componentRef.setInput('chart', {
      points: [
        {
          x: 100,
          y: 120,
          color: 'var(--theme-accent)',
          tooltipLines: ['2026-05-01', 'media files: 10', 'Change: +10 media files'],
          occurredAt: '2026-05-01T00:00:00Z',
        },
        {
          x: 250,
          y: 60,
          color: 'var(--theme-accent)',
          tooltipLines: ['2026-05-02', 'media files: 12', 'Change: +2 media files'],
          occurredAt: '2026-05-02T00:00:00Z',
        },
      ],
      linePath: 'M 100 120 L 250 60',
      areaPath: 'M 100 304 L 100 120 L 250 60 L 250 304 Z',
      yTicks: [{ y: 120, label: '10' }],
      xLabels: [{ x: 100, label: 'May 1', anchor: 'start' }],
    });
  });

  function setFullChart(): void {
    const points = Array.from({ length: 5 }, (_, index) => ({
      x: 72 + index * 216,
      y: 200 - index * 20,
      color: 'green',
      occurredAt: `2026-05-0${index + 1}T00:00:00Z`,
      tooltipLines: [`May ${index + 1}`, `Value ${index}`],
    }));
    fixture.componentRef.setInput('chart', {
      points,
      linePath: 'original line',
      areaPath: 'original area',
      yTicks: [{ y: 200, label: '10' }],
      xLabels: [
        { x: 72, label: 'May 1', anchor: 'start' },
        { x: 936, label: 'May 5', anchor: 'end' },
      ],
    });
    fixture.detectChanges();
  }

  function mockChartBounds(): SVGSVGElement {
    const svg = fixture.nativeElement.querySelector('svg') as SVGSVGElement;
    spyOn(svg, 'getBoundingClientRect').and.returnValue({ left: 0, top: 0, width: 960, height: 360 } as DOMRect);
    return svg;
  }

  it('zooms the time axis, updates date labels, and clips paths without scaling the value axis', () => {
    setFullChart();
    const widget = fixture.componentInstance;
    widget['changeZoom'](2);
    fixture.detectChanges();
    const chart = widget['displayChart']()!;
    expect(widget['viewport']()).toEqual({ start: 0.25, end: 0.75 });
    expect(chart.points.map((point) => point.x)).toEqual([-360, 72, 504, 936, 1368]);
    expect(chart.points.map((point) => point.y)).toEqual([200, 180, 160, 140, 120]);
    expect(chart.xLabels.map((label) => label.label)).toEqual(['May 2', 'May 4']);
    expect(fixture.nativeElement.querySelector('g[clip-path]').getAttribute('clip-path')).toBe('url(#trend-plot-clip-media)');
    expect(chart.linePath).toContain('L 504 160');
  });

  it('zooms around the mouse position and prevents page scrolling only over the plot', () => {
    setFullChart();
    const svg = mockChartBounds();
    const wheel = new WheelEvent('wheel', { clientX: 936, deltaY: -100, cancelable: true, bubbles: true });
    svg.dispatchEvent(wheel);
    expect(wheel.defaultPrevented).toBeTrue();
    expect(fixture.componentInstance['viewport']().start).toBeCloseTo(0.2);
    expect(fixture.componentInstance['viewport']().end).toBe(1);
    const outside = new WheelEvent('wheel', { clientX: 20, deltaY: -100, cancelable: true });
    svg.dispatchEvent(outside);
    expect(outside.defaultPrevented).toBeFalse();
  });

  it('keeps panning and zoom within the available time range and resets on new data', () => {
    setFullChart();
    const widget = fixture.componentInstance;
    const original = widget.chart();
    widget['changeZoom'](1000);
    expect(widget['viewport']().end - widget['viewport']().start).toBe(0.25);
    widget['pan'](100);
    expect(widget['viewport']()).toEqual({ start: 0.75, end: 1 });
    widget['pan'](-100);
    expect(widget['viewport']()).toEqual({ start: 0, end: 0.25 });
    fixture.componentRef.setInput('chart', { ...original! });
    expect(widget['viewport']()).toEqual({ start: 0, end: 1 });
    widget['changeZoom'](2);
    widget['resetZoom']();
    expect(widget['displayChart']()).toBe(widget.chart());
  });

  it('moves the visible range while dragging and clears the tooltip', () => {
    setFullChart();
    const svg = mockChartBounds();
    spyOn(svg, 'setPointerCapture');
    const widget = fixture.componentInstance;
    widget['changeZoom'](2);
    fixture.detectChanges();
    svg.dispatchEvent(new PointerEvent('pointerdown', { pointerId: 1, clientX: 504, button: 0 }));
    svg.dispatchEvent(new PointerEvent('pointermove', { pointerId: 1, clientX: 720 }));
    expect(widget['viewport']()).toEqual({ start: 0.125, end: 0.625 });
    expect(widget['hoverTooltip']()).toBeNull();
    svg.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1 }));
    svg.dispatchEvent(new PointerEvent('pointermove', { pointerId: 1, clientX: 800 }));
    expect(widget['viewport']()).toEqual({ start: 0.125, end: 0.625 });
  });

  it('uses the transformed points for hover tooltips and exposes accessible zoom buttons', () => {
    setFullChart();
    const widget = fixture.componentInstance;
    widget['changeZoom'](2);
    fixture.detectChanges();
    const svg = mockChartBounds();
    svg.dispatchEvent(new MouseEvent('mousemove', { clientX: 504, clientY: 160 }));
    fixture.detectChanges();
    expect(widget['hoverTooltip']()?.pointX).toBe(504);
    expect(widget['hoverTooltip']()?.lines).toEqual(['May 3', 'Value 2']);
    const reset = fixture.nativeElement.querySelector('button[aria-label="Reset zoom"]') as HTMLButtonElement;
    reset.click();
    fixture.detectChanges();
    expect(widget['zoomed']()).toBeFalse();
    expect(widget['hoverTooltip']()).toBeNull();
    expect(reset.disabled).toBeTrue();
  });

  it('disables zoom when there is only one data point', () => {
    setFullChart();
    const widget = fixture.componentInstance;
    fixture.componentRef.setInput('chart', { ...widget.chart()!, points: [widget.chart()!.points[0]] });
    fixture.detectChanges();
    widget['changeZoom'](2);
    expect(widget['viewport']()).toEqual({ start: 0, end: 1 });
    expect(widget['canZoomIn']()).toBeFalse();
  });

  it('emits the nearest point timestamp when a chart point is clicked', () => {
    setFullChart();
    const widget = fixture.componentInstance;
    const emitted: string[] = [];
    widget.pointSelected.subscribe((occurredAt: string) => emitted.push(occurredAt));
    const svg = mockChartBounds();
    svg.dispatchEvent(new MouseEvent('click', { clientX: 504, clientY: 160 }));
    expect(emitted).toEqual(['2026-05-03T00:00:00Z']);
  });

  it('does not emit a selected point while clicking outside the plot area', () => {
    setFullChart();
    const widget = fixture.componentInstance;
    const emitted: string[] = [];
    widget.pointSelected.subscribe((occurredAt: string) => emitted.push(occurredAt));
    const svg = mockChartBounds();
    svg.dispatchEvent(new MouseEvent('click', { clientX: 20, clientY: 160 }));
    expect(emitted).toEqual([]);
  });

  it('suppresses the point selection after a panning drag', () => {
    setFullChart();
    const svg = mockChartBounds();
    spyOn(svg, 'setPointerCapture');
    const widget = fixture.componentInstance;
    const emitted: string[] = [];
    widget.pointSelected.subscribe((occurredAt: string) => emitted.push(occurredAt));
    widget['changeZoom'](2);
    fixture.detectChanges();
    svg.dispatchEvent(new PointerEvent('pointerdown', { pointerId: 1, clientX: 504, button: 0 }));
    svg.dispatchEvent(new PointerEvent('pointermove', { pointerId: 1, clientX: 620 }));
    svg.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1 }));
    svg.dispatchEvent(new MouseEvent('click', { clientX: 620, clientY: 160 }));
    expect(emitted).toEqual([]);
    svg.dispatchEvent(new MouseEvent('click', { clientX: 620, clientY: 160 }));
    expect(emitted).toHaveSize(1);
  });

  it('renders the chart title and tooltip on hover', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const svg: SVGElement | null = fixture.nativeElement.querySelector('svg');
    expect(svg).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Media changes');

    spyOn(svg!, 'getBoundingClientRect').and.returnValue({
      x: 0,
      y: 0,
      left: 0,
      top: 0,
      right: 960,
      bottom: 360,
      width: 960,
      height: 360,
      toJSON: () => ({}),
    } as DOMRect);

    svg?.dispatchEvent(new MouseEvent('mouseenter', { clientX: 120, clientY: 120, bubbles: true }));
    svg?.dispatchEvent(new MouseEvent('mousemove', { clientX: 120, clientY: 120, bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.trend-tooltip')).toBeTruthy();
  });
});
