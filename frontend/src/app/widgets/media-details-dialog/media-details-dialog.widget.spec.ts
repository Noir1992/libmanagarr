import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MediaDetailData } from '../../interfaces/general/media-detail-data';
import { MediaGridItem } from '../../interfaces/general/media-grid-item';
import { AppSettingsService } from '../../services/app-settings.service';
import { MediaCardWidget } from '../media-card/media-card.widget';
import { SonarrSeriesDialogWidget } from '../sonarr-series-dialog/sonarr-series-dialog.widget';
import { MediaDetailsDialogWidget } from './media-details-dialog.widget';

const movie: MediaDetailData = {
  title: 'Movie',
  url: 'http://radarr/movie/movie',
  instanceName: 'Radarr',
  hasFile: true,
  monitored: false,
  hasAired: true,
  status: 'released',
  qualityProfileName: 'HD',
  customFormatScore: 0,
  upgradeUntilCustomFormatScore: 0,
  genres: ['Drama'],
  tags: ['favorite'],
  mediaDetails: {
    resolution: '1080p',
    originalResolution: '1920x800',
    videoCodec: 'HEVC',
    audioCodec: 'EAC3',
    dynamicRange: 'HDR10',
    containerFormat: 'mkv',
    videoBitrate: 8000000,
    audioLanguages: ['English'],
    subtitleLanguages: ['German'],
    size: 1073741824,
    durationSeconds: 3600,
    releaseGroup: 'GROUP',
    relativePath: 'Movie/movie.mkv',
  },
};

describe('Media detail dialogs', () => {
  it('renders filterable movie metadata, formatted file information and zero scores', async () => {
    await TestBed.configureTestingModule({
      imports: [MediaDetailsDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: movie },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(MediaDetailsDialogWidget);
    fixture.detectChanges();
    const ddText = (dd: HTMLElement) => {
      const clone = dd.cloneNode(true) as HTMLElement;
      clone.querySelectorAll('.instance-link').forEach((link) => link.remove());
      return clone.textContent!.trim();
    };
    const rows: Record<string, string> = {};
    (fixture.nativeElement as HTMLElement)
      .querySelectorAll('.detail-row')
      .forEach((row) => (rows[row.querySelector('dt')!.textContent!.trim()] = ddText(row.querySelector('dd')!)));
    expect(rows).toEqual(
      jasmine.objectContaining({
        Instance: 'Radarr',
        Monitored: 'No',
        Score: '0',
        'Target score': '0',
        'Upgrade target': 'Target score reached/exceeded',
        Genres: 'Drama',
        Tags: 'favorite',
        Resolution: '1080p',
        'Original resolution': '1920x800',
        'Video codec': 'HEVC',
        'Audio codec': 'EAC3',
        'Dynamic range': 'HDR10',
        'Container format': 'mkv',
        'Video bitrate (Mbit/s)': '8',
        'Audio languages': 'English',
        'Subtitle languages': 'German',
        'File size': '1 GiB',
        Duration: '1:00:00',
        'Release group': 'GROUP',
        'File path': 'Movie/movie.mkv',
      }),
    );
  });

  it('renders the poster above the details when posterUrl is set', async () => {
    await TestBed.configureTestingModule({
      imports: [MediaDetailsDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { ...movie, posterUrl: 'http://localhost/api/poster/1.jpg' } },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(MediaDetailsDialogWidget);
    fixture.detectChanges();
    const poster = (fixture.nativeElement as HTMLElement).querySelector('img.detail-poster');
    expect(poster?.getAttribute('src')).toBe('http://localhost/api/poster/1.jpg');
    const wrapper = (fixture.nativeElement as HTMLElement).querySelector('.detail-poster-wrapper')!;
    expect(wrapper.querySelector('img.detail-poster')).not.toBeNull();
    const detailList = (fixture.nativeElement as HTMLElement).querySelector('.detail-list')!;
    expect(wrapper.compareDocumentPosition(detailList) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('renders no poster when posterUrl is absent', async () => {
    await TestBed.configureTestingModule({
      imports: [MediaDetailsDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: movie },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(MediaDetailsDialogWidget);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('img.detail-poster')).toBeNull();
  });

  it('keeps missing episode scores unavailable and shows episode metadata without series metadata', () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            ...movie,
            hasFile: false,
            seasonNumber: 0,
            episodeNumber: 1,
            seriesTitle: 'Series',
            seriesMonitored: true,
            monitored: false,
          },
        },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    });
    const dialog = TestBed.runInInjectionContext(() => new MediaDetailsDialogWidget());
    const rows = Object.fromEntries(dialog['rows']().map((row) => [row.label, row.value]));
    expect(rows['Score']).toBe('N/A');
    expect(rows['Resolution']).toBe('N/A');
    expect(rows['Upgrade target']).toBe('N/A');
    for (const label of ['Instance', 'Series', 'Series monitored', 'Aired status', 'Profile', 'Genres', 'Tags']) {
      expect(rows[label]).withContext(label).toBeUndefined();
    }
    expect(rows['Season']).toBe('0');
    expect(rows['Episode']).toBe('1');
    expect(rows['Target score']).toBe('0');
    expect(rows['Monitored']).toBe('No');
    expect(rows['Type']).toBe('Specials');
  });

  it('opens movie details from the card and episode details from the series dialog', async () => {
    const open = jasmine.createSpy('open');
    const item: MediaGridItem = {
      kind: 'movie',
      id: 1,
      title: movie.title,
      url: movie.url,
      posterUrl: null,
      detailRows: [],
      movieData: movie,
    };
    await TestBed.configureTestingModule({
      imports: [MediaCardWidget, SonarrSeriesDialogWidget],
      providers: [
        { provide: MatDialog, useValue: { open } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            ...item,
            kind: 'series',
            seriesData: {
              seasons: [
                { number: 1, episodes: [{ number: 1, title: 'Episode', details: { ...movie, title: 'Episode', episodeNumber: 1 } }] },
              ],
            },
          },
        },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    TestBed.overrideProvider(MatDialog, { useValue: { open } });
    const card = TestBed.createComponent(MediaCardWidget);
    card.componentRef.setInput('item', item);
    card.componentRef.setInput('clickable', true);
    card.detectChanges();
    const titleLink = (card.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('.media-card-title a')!;
    expect(titleLink.href).toBe(movie.url);
    expect(titleLink.target).toBe('_blank');
    const linkClick = new MouseEvent('click', { bubbles: true, cancelable: true });
    linkClick.preventDefault();
    titleLink.dispatchEvent(linkClick);
    expect(open).not.toHaveBeenCalled();
    (card.nativeElement as HTMLElement).querySelector<HTMLElement>('.media-card')!.click();
    expect(open).toHaveBeenCalledOnceWith(MediaDetailsDialogWidget, jasmine.objectContaining({ data: movie }));
    open.calls.reset();
    const series = TestBed.createComponent(SonarrSeriesDialogWidget);
    series.detectChanges();
    (series.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.episode-details-button')!.click();
    expect(open).toHaveBeenCalledOnceWith(
      MediaDetailsDialogWidget,
      jasmine.objectContaining({ data: jasmine.objectContaining({ title: 'Episode', episodeNumber: 1 }) }),
    );
  });
  it('shows Added for a missing movie but keeps it out of episode details', () => {
    const data = { ...movie, hasFile: false, addedAt: '2026-01-10T12:00:00Z' };
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    });
    const rows = TestBed.runInInjectionContext(() => new MediaDetailsDialogWidget())['rows']();
    expect(rows.find((row) => row.key === 'filter.addedRange')?.value).toContain('Jan 10, 2026');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { ...data, episodeNumber: 1, seasonNumber: 1 } },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    });
    const episodeRows = TestBed.runInInjectionContext(() => new MediaDetailsDialogWidget())['rows']();
    expect(episodeRows.find((row) => row.key === 'filter.addedRange')).toBeUndefined();
  });

  it('marks aired status and availability with the tile success tones', async () => {
    await TestBed.configureTestingModule({
      imports: [MediaDetailsDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: movie },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(MediaDetailsDialogWidget);
    fixture.detectChanges();
    const ddFor = (label: string) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.detail-row'))
        .find((row) => row.querySelector('dt')?.textContent?.trim() === label)!
        .querySelector('dd')!;
    expect(ddFor('Aired status').classList).toContain('tone-success');
    expect(ddFor('Availability').classList).toContain('tone-success');
  });

  it('marks an unreleased movie without file with warning and danger tones', async () => {
    await TestBed.configureTestingModule({
      imports: [MediaDetailsDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { ...movie, status: 'announced', hasFile: false } },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(MediaDetailsDialogWidget);
    fixture.detectChanges();
    const ddFor = (label: string) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.detail-row'))
        .find((row) => row.querySelector('dt')?.textContent?.trim() === label)!
        .querySelector('dd')!;
    expect(ddFor('Aired status').classList).toContain('tone-warning');
    expect(ddFor('Availability').classList).toContain('tone-danger');
  });

  it('links the instance through an icon behind the plain instance name', async () => {
    await TestBed.configureTestingModule({
      imports: [MediaDetailsDialogWidget],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: movie },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
        { provide: AppSettingsService, useValue: { localeId: signal('en-US') } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(MediaDetailsDialogWidget);
    fixture.detectChanges();
    const dd = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.detail-row'))
      .find((row) => row.querySelector('dt')?.textContent?.trim() === 'Instance')!
      .querySelector('dd')!;
    expect(dd.textContent).toContain('Radarr');
    const link = dd.querySelector<HTMLAnchorElement>('a.instance-link')!;
    expect(link.href).toBe(movie.url);
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
    expect(link.getAttribute('aria-label')).toBe('Open in Radarr');
    expect(link.querySelector('span.material-symbols-outlined')).not.toBeNull();
    expect(link.textContent).not.toContain('Radarr');
  });
});
