import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatisticsChartCategory, StatisticsChartExpandedItem } from '../../interfaces/codec-statistic/statistics-chart-item';
import { AppI18nService } from '../../services/app-i18n.service';
import { StatisticsChartWidget } from './statistics-chart.widget';

describe('StatisticsChartWidget value list', () => {
  let fixture: ComponentFixture<StatisticsChartWidget>;
  let element: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatisticsChartWidget] }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(StatisticsChartWidget);
    const category: StatisticsChartCategory<StatisticsChartExpandedItem> = {
      key: 'videoCodecs',
      title: 'Video codecs',
      items: ['HEVC', 'AVC'].map((label) => ({
        label,
        count: 1,
        percentage: 50,
        color: '#4ea8de',
        dashArray: '157 157',
        dashOffset: '0',
        tooltip: label,
        metaText: '1 media file',
        treeRoots: [],
      })),
    };
    fixture.componentRef.setInput('category', category);
    await fixture.whenStable();
    element = fixture.nativeElement;
  });

  it('starts collapsed and lets the user show and hide the values', async () => {
    const toggle = element.querySelector<HTMLButtonElement>('.chart-values-toggle')!;
    expect(element.querySelector('app-statistics-chart-rows')).toBeNull();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.textContent).toContain('Show values');
    expect(toggle.querySelector('.chart-values-count')!.textContent!.trim()).toBe('(2)');
    toggle.click();
    await fixture.whenStable();
    expect(element.querySelectorAll('.chart-row-button')).toHaveSize(2);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.textContent).toContain('Hide values');
    toggle.click();
    await fixture.whenStable();
    expect(element.querySelector('app-statistics-chart-rows')).toBeNull();
  });

  it('opens the list and the selected value when a chart segment is clicked', async () => {
    element.querySelectorAll('.pie-segment')[1].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await fixture.whenStable();
    const rows = element.querySelectorAll<HTMLButtonElement>('.chart-row-button');
    expect(rows[0].classList.contains('expanded')).toBeFalse();
    expect(rows[1].classList.contains('expanded')).toBeTrue();
    expect(element.querySelectorAll('app-media-files-tree')).toHaveSize(1);
    expect(element.querySelector('.chart-values-toggle')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('collapses an expanded value on another segment click and keeps the list visible', async () => {
    const segment = element.querySelector('.pie-segment')!;
    segment.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await fixture.whenStable();
    segment.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await fixture.whenStable();
    expect(element.querySelector('.chart-row-button')!.classList.contains('expanded')).toBeFalse();
    expect(element.querySelector('.chart-values-toggle')!.getAttribute('aria-expanded')).toBe('true');
    element.querySelector<HTMLButtonElement>('.chart-values-toggle')!.click();
    await fixture.whenStable();
    segment.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await fixture.whenStable();
    expect(element.querySelector('.chart-row-button')!.classList.contains('expanded')).toBeTrue();
    expect(element.querySelector('.chart-values-toggle')!.getAttribute('aria-expanded')).toBe('true');
  });
});
