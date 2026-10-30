import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { LibrarySortDirection } from '../../enums/library-sort-direction';
import { LibrarySortField } from '../../enums/library-sort-field';
import { AppI18nService } from '../../services/app-i18n.service';
import { SortControlWidget } from './sort-control.widget';

describe('SortControlWidget', () => {
  let fixture: ComponentFixture<SortControlWidget>;
  let widget: SortControlWidget;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SortControlWidget] }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(SortControlWidget);
    widget = fixture.componentInstance;
    fixture.componentRef.setInput('sortField', LibrarySortField.ALPHABETICAL);
    fixture.componentRef.setInput('sortDirection', LibrarySortDirection.ASCENDING);
    fixture.detectChanges();
  });

  async function openMenu(): Promise<MatMenuHarness> {
    const menu = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatMenuHarness);
    await menu.open();
    return menu;
  }

  async function itemTexts(menu: MatMenuHarness): Promise<string[]> {
    return Promise.all((await menu.getItems()).map(async (item) => (await item.getText()).replace(/\s+/g, ' ').trim()));
  }

  function collectEmits(): { fields: LibrarySortField[]; directions: LibrarySortDirection[] } {
    const emits = { fields: [] as LibrarySortField[], directions: [] as LibrarySortDirection[] };
    widget.sortFieldChange.subscribe((value: LibrarySortField) => emits.fields.push(value));
    widget.sortDirectionChange.subscribe((value: LibrarySortDirection) => emits.directions.push(value));
    return emits;
  }

  it('lists the four sortable fields and marks the active one with the current direction', async () => {
    const texts = await itemTexts(await openMenu());

    expect(texts).toHaveSize(4);
    expect(texts[0]).toContain('Alphabetical');
    expect(texts[0]).toContain('arrow_upward');
    expect(texts[1]).toContain('Air date');
    expect(texts[1]).not.toContain('arrow_');
    expect(texts[2]).toContain('Added');
    expect(texts[2]).not.toContain('arrow_');
    expect(texts[3]).toContain('Size');
    expect(texts[3]).not.toContain('arrow_');
  });

  it('emits the picked field and keeps the direction while already sorting ascending', async () => {
    const emits = collectEmits();
    const menu = await openMenu();
    const items = await menu.getItems();
    await items[1].click();

    expect(emits.fields).toEqual([LibrarySortField.AIRED]);
    expect(emits.directions).toEqual([]);
  });

  it('emits size when the size option is picked', async () => {
    const emits = collectEmits();
    const items = await (await openMenu()).getItems();
    await items[3].click();
    expect(emits.fields).toEqual([LibrarySortField.SIZE]);
  });

  it('resets to ascending when another field is picked while sorting descending', async () => {
    fixture.componentRef.setInput('sortDirection', LibrarySortDirection.DESCENDING);
    fixture.detectChanges();
    const emits = collectEmits();

    const menu = await openMenu();
    const items = await menu.getItems();
    await items[1].click();

    expect(emits.fields).toEqual([LibrarySortField.AIRED]);
    expect(emits.directions).toEqual([LibrarySortDirection.ASCENDING]);
  });

  it('flips the direction when the active field is picked again', async () => {
    const emits = collectEmits();

    const menu = await openMenu();
    const items = await menu.getItems();
    await items[0].click();

    expect(emits.fields).toEqual([]);
    expect(emits.directions).toEqual([LibrarySortDirection.DESCENDING]);

    fixture.componentRef.setInput('sortDirection', LibrarySortDirection.DESCENDING);
    fixture.detectChanges();
    expect((await itemTexts(await openMenu()))[0]).toContain('arrow_downward');
  });
});
