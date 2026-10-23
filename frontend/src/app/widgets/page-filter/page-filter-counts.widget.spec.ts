import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { FilterOptionType } from '../../enums/filter-option-type';
import { FilterOption } from '../../interfaces/filter-option';
import { AppI18nService } from '../../services/app-i18n.service';
import { PageFilterWidget } from './page-filter.widget';

describe('PageFilterWidget option counts', () => {
  let fixture: ComponentFixture<PageFilterWidget>;
  const storageKey = 'filters:option-count-test';

  beforeEach(async () => {
    localStorage.removeItem(storageKey);
    await TestBed.configureTestingModule({ imports: [PageFilterWidget] }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(PageFilterWidget);
    fixture.componentRef.setInput('storagePrefix', 'option-count-test');
    fixture.componentInstance['expanded'].set(true);
  });

  afterEach(() => localStorage.removeItem(storageKey));

  async function selectFor(filter: FilterOption): Promise<MatSelectHarness> {
    fixture.componentRef.setInput('filters', [filter]);
    fixture.detectChanges();
    return TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness);
  }

  it('shows counts including zero, aligns them on the right, and keeps them out of the selected text', async () => {
    const select = await selectFor({
      id: 'video-codec-filter',
      label: 'Video codec',
      type: FilterOptionType.MULTISELECT,
      options: [
        { value: 'HEVC', label: 'HEVC', count: 3 },
        { value: 'AV1', label: 'AV1', count: 0 },
      ],
      totalCount: 5,
      selectedValues: [],
      onChanged: jasmine.createSpy('changed'),
    });
    await select.open();
    const options = await select.getOptions();
    expect(await Promise.all(options.map((option) => option.getText()))).toEqual(['All(5)', 'HEVC(3)', 'AV1(0)']);
    const counts = Array.from(document.querySelectorAll<HTMLElement>('.filter-option-count'));
    const rightEdges = counts.map((count) => count.getBoundingClientRect().right);
    expect(counts).toHaveSize(3);
    expect(Math.max(...rightEdges) - Math.min(...rightEdges)).toBeLessThan(1);
    await options[1].click();
    await select.close();
    expect(await select.getValueText()).toBe('HEVC');
  });

  it('shows counts in single selects while keeping the selected label unchanged', async () => {
    const select = await selectFor({
      id: 'monitored-filter',
      label: 'Monitored',
      type: FilterOptionType.SELECT,
      options: [
        { value: 'all', label: 'All', count: 5 },
        { value: 'monitored', label: 'Monitored', count: 3 },
      ],
      selectedValue: 'all',
      onChanged: jasmine.createSpy('changed'),
    });
    await select.open();
    const options = await select.getOptions();
    expect(await options[1].getText()).toBe('Monitored(3)');
    await options[1].click();
    expect(await select.getValueText()).toBe('Monitored');
  });

  it('keeps ordinary selects without count data free of brackets', async () => {
    const select = await selectFor({
      id: 'plain',
      label: 'Plain',
      type: FilterOptionType.MULTISELECT,
      options: [{ value: 'one', label: 'One' }],
      selectedValues: [],
      onChanged: jasmine.createSpy('changed'),
    });
    await select.open();
    const options = await select.getOptions();
    expect(await Promise.all(options.map((option) => option.getText()))).toEqual(['All', 'One']);
    expect(document.querySelector('.filter-option-count')).toBeNull();
  });
});
