import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MediaGridItem } from '../../interfaces/general/media-grid-item';
import { AppI18nService } from '../../services/app-i18n.service';
import { SonarrSeriesDialogWidget } from './sonarr-series-dialog.widget';

describe('SonarrSeriesDialogWidget episode table', () => {
  for (const [size, expected] of [
    [2.5 * 1024 ** 3, '2.5 GiB'],
    [0, '0 GiB'],
    [null, 'N/A'],
  ] as const) {
    it(`shows the complete series size as ${expected}`, async () => {
      const item: MediaGridItem = {
        kind: 'series',
        id: 1,
        title: 'Series',
        url: '',
        posterUrl: null,
        detailRows: [],
        size,
        seriesData: { seasons: [] },
      };
      await TestBed.configureTestingModule({
        imports: [SonarrSeriesDialogWidget],
        providers: [
          { provide: MAT_DIALOG_DATA, useValue: item },
          { provide: MatDialogRef, useValue: { close: () => undefined } },
        ],
      }).compileComponents();
      TestBed.inject(AppI18nService).setLanguage('en');
      const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
      fixture.detectChanges();
      const row = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.series-dialog-info-row')).find(
        (row) => row.querySelector('dt')?.textContent?.trim() === 'Total size',
      );
      expect(row?.querySelector('dd')?.textContent?.trim()).toBe(expected);
    });
  }

  it('shows missing for an upcoming series without files', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Upcoming',
      url: '',
      posterUrl: null,
      detailRows: [],
      seriesData: { seasons: [], status: 'upcoming', isAvailable: false },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const availability = Array.from(element.querySelectorAll('.series-dialog-info-row')).find(
      (row) => row.querySelector('dt')?.textContent?.trim() === 'Availability',
    );
    expect(availability?.querySelector('dd')?.textContent?.trim()).toBe('Missing');
  });

  it('shows episode number, title and air date for a missing episode', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: '',
      posterUrl: null,
      detailRows: [],
      seriesData: { seasons: [{ number: 1, episodes: [{ number: 1, title: 'Missing', hasFile: false, customFormatScore: null }] }] },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(Array.from(element.querySelectorAll('thead th')).map((cell) => cell.textContent?.trim())).toEqual([
      'Episode',
      'Title',
      'Air date',
    ]);
    expect(Array.from(element.querySelectorAll('tbody td')).map((cell) => cell.textContent?.trim())).toEqual(['1', 'Missing', 'N/A']);
  });
  for (const target of [0, null]) {
    it(`keeps the episode table compact when the target score is ${target}`, async () => {
      const item: MediaGridItem = {
        kind: 'series',
        id: 1,
        title: 'Series',
        url: '',
        posterUrl: null,
        detailRows: [],
        seriesData: {
          instanceName: 'My Sonarr',
          qualityProfileName: 'HD',
          status: 'continuing',
          isAvailable: false,
          monitored: true,
          genres: ['Drama'],
          tags: ['favorite'],
          upgradeUntilCustomFormatScore: target,
          seasons: [{ number: 1, episodes: [{ number: 1, title: 'Episode', customFormatScore: -1, airDateUtc: '2020-01-02T12:00:00Z' }] }],
        },
      };
      await TestBed.configureTestingModule({
        imports: [SonarrSeriesDialogWidget],
        providers: [
          { provide: MAT_DIALOG_DATA, useValue: item },
          { provide: MatDialogRef, useValue: { close: () => undefined } },
        ],
      }).compileComponents();
      TestBed.inject(AppI18nService).setLanguage('en');
      const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
      fixture.detectChanges();
      const element: HTMLElement = fixture.nativeElement;
      expect(Array.from(element.querySelectorAll('thead th')).map((cell) => cell.textContent?.trim())).toEqual([
        'Episode',
        'Title',
        'Air date',
      ]);
      expect(Array.from(element.querySelectorAll('tbody td')).map((cell) => cell.textContent?.trim())).toEqual([
        '1',
        'Episode',
        '2020-01-02',
      ]);
      const rows = Object.fromEntries(
        Array.from(element.querySelectorAll('.series-dialog-info-row')).map((row) => [
          row.querySelector('dt')!.textContent!.trim(),
          row.querySelector('dd')!.textContent!.trim(),
        ]),
      );
      expect(rows).toEqual({
        Instance: 'My Sonarr',
        Profile: 'HD',
        'Aired status': 'continuing',
        Added: 'N/A',
        Availability: 'Missing',
        'Total size': 'N/A',
        'Series monitored': 'Yes',
        Genres: 'Drama',
        Tags: 'favorite',
      });
    });
  }
  it('shows the poster beside the series info and keeps the seasons below it', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: '',
      posterUrl: 'http://localhost/api/sonarr/instances/1/poster/1?w=342',
      detailRows: [],
      seriesData: { seasons: [{ number: 1, episodes: [{ number: 1 }] }] },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    const poster = element.querySelector('img.series-dialog-poster');
    expect(poster?.getAttribute('src')).toBe('http://localhost/api/sonarr/instances/1/poster/1?w=342');
    const top = element.querySelector('.series-dialog-top');
    expect(top?.contains(poster)).toBe(true);
    expect(top?.contains(element.querySelector('.series-dialog-info'))).toBe(true);
    const season = element.querySelector('.series-dialog-season');
    expect(season).not.toBeNull();
    expect(top?.contains(season)).toBe(false);
    expect(top!.compareDocumentPosition(season!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('shows the series addition date separately from episode air dates', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: '',
      posterUrl: null,
      detailRows: [],
      seriesData: {
        addedAt: '2026-01-10T12:00:00Z',
        seasons: [{ number: 1, episodes: [{ number: 1, airDateUtc: '2020-01-02T12:00:00Z' }] }],
      },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const added = fixture.componentInstance['seriesRows']().find((row) => row.key === 'filter.addedRange');
    expect(added?.label).toBe('Added');
    expect(added?.value).toContain('Jan 10, 2026');
    expect((fixture.nativeElement as HTMLElement).querySelector('tbody')?.textContent).toContain('2020-01-02');
  });

  it('marks aired status and availability with the tile tones for continuing and available series', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: '',
      posterUrl: null,
      detailRows: [],
      seriesData: { seasons: [], status: 'continuing', isAvailable: true },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const ddFor = (label: string) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.series-dialog-info-row'))
        .find((row) => row.querySelector('dt')?.textContent?.trim() === label)!
        .querySelector('dd')!;
    expect(ddFor('Aired status').classList).toContain('tone-info');
    expect(ddFor('Availability').classList).toContain('tone-success');
  });

  it('keeps aired status and availability uncolored when the series values are unknown', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: '',
      posterUrl: null,
      detailRows: [],
      seriesData: { seasons: [] },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const tones = ['tone-success', 'tone-warning', 'tone-danger', 'tone-info'];
    for (const label of ['Aired status', 'Availability']) {
      const dd = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.series-dialog-info-row'))
        .find((row) => row.querySelector('dt')?.textContent?.trim() === label)!
        .querySelector('dd')!;
      expect(tones.some((tone) => dd.classList.contains(tone)))
        .withContext(label)
        .toBe(false);
    }
  });

  it('links the instance through an icon behind the plain instance name', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: 'http://sonarr/series/test',
      posterUrl: null,
      detailRows: [],
      seriesData: { instanceName: 'My Sonarr', seasons: [] },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const infoDd = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.series-dialog-info-row'))
      .find((row) => row.querySelector('dt')?.textContent?.trim() === 'Instance')!
      .querySelector('dd')!;
    expect(infoDd.textContent).toContain('My Sonarr');
    const link = infoDd.querySelector<HTMLAnchorElement>('a.instance-link')!;
    expect(link.href).toBe('http://sonarr/series/test');
    expect(link.target).toBe('_blank');
    expect(link.getAttribute('aria-label')).toBe('Open in My Sonarr');
    expect(link.querySelector('span.material-symbols-outlined')).not.toBeNull();
    expect(link.textContent).not.toContain('My Sonarr');
  });

  it('labels season 0 as Specials', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: '',
      posterUrl: null,
      detailRows: [],
      seriesData: { seasons: [{ number: 0, episodes: [{ number: 1 }] }] },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const label = (fixture.nativeElement as HTMLElement).querySelector('.series-dialog-season-label')!.textContent!;
    expect(label).toContain('Specials');
    expect(label).not.toContain('Season 0');
  });

  it('renders the specials table after all numbered seasons', async () => {
    const item: MediaGridItem = {
      kind: 'series',
      id: 1,
      title: 'Series',
      url: '',
      posterUrl: null,
      detailRows: [],
      seriesData: {
        seasons: [
          { number: 1, episodes: [{ number: 1 }] },
          { number: 0, episodes: [{ number: 1 }] },
          { number: 2, episodes: [{ number: 1 }] },
        ],
      },
    };
    await TestBed.configureTestingModule({
      imports: [SonarrSeriesDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: item },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    const fixture = TestBed.createComponent(SonarrSeriesDialogWidget);
    fixture.detectChanges();
    const titles = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.series-dialog-season-label')).map((label) =>
      label.textContent!.split('(')[0].trim(),
    );
    expect(titles).toEqual(['Season 1', 'Season 2', 'Specials']);
  });
});
