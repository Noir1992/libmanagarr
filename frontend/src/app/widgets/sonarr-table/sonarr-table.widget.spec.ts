import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';
import { SonarrLibraryEpisodeTable } from '../../interfaces/library/sonarr-library-episode-table';
import { SonarrLibrarySeasonTable } from '../../interfaces/library/sonarr-library-season-table';
import { SonarrLibraryTable } from '../../interfaces/library/sonarr-library-table';
import { SonarrLibrarySeriesTable } from '../../interfaces/library/sonarr-library-series-table';
import { TableColumnTemplateDirective } from '../table-column-template.directive';
import { SonarrTableWidget } from './sonarr-table.widget';

@Component({
  imports: [SonarrTableWidget, TableColumnTemplateDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-sonarr-table [table]="table">
      <ng-template appTableColumn [appTableColumnConfig]="{ kind: 'series', key: 'title', header: 'Serie', showExpandIcon: false }" let-row>
        @if (getSeriesUrl(row); as seriesUrl) {
          <a class="text-link" [href]="seriesUrl">{{ row.title }}</a>
        } @else {
          {{ row.title }}
        }
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{
          kind: 'series',
          key: 'affectedSeasons',
          header: 'Betroffene Staffeln',
          showExpandIcon: false,
        }"
        let-row
      >
        {{ row.affectedSeasons }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{
          kind: 'series',
          key: 'affectedEpisodes',
          header: 'Betroffene Folgen',
          showExpandIcon: false,
        }"
        let-row
      >
        {{ row.affectedEpisodes }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'series', key: 'qualityProfileName', header: 'Profil', showExpandIcon: false }"
        let-row
      >
        {{ row.qualityProfileName }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{
          kind: 'series',
          key: 'upgradeUntilCustomFormatScore',
          header: 'Gewuenschter Score',
          showExpandIcon: false,
        }"
        let-row
      >
        {{ row.upgradeUntilCustomFormatScore }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'season', key: 'number', header: 'Season', showExpandIcon: false }"
        let-row
      >
        {{ row.seasonNumber }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'episode', key: 'number', header: 'Folge', showExpandIcon: false }"
        let-row
      >
        {{ row.episodeNumber }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'episode', key: 'title', header: 'Titel', showExpandIcon: false }"
        let-row
      >
        {{ row.episodeTitle }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'episode', key: 'customFormatScore', header: 'Score', showExpandIcon: false }"
        let-row
      >
        {{ row.customFormatScore }}
      </ng-template>
    </app-sonarr-table>
  `,
})
class SonarrTableHostComponent {
  table: SonarrLibraryTable = {
    instanceName: 'Sonarr',
    instanceId: 'sonarr-1',
    syncedAt: '2026-05-30T21:42:04.0728462Z',
    series: [
      {
        url: 'http://localhost:8989/series/the-expanse',
        id: 42,
        title: 'The Expanse',
        titleSlug: 'the-expanse',
        affectedSeasons: 1,
        affectedEpisodes: 1,
        qualityProfileName: 'HD',
        status: 'continuing',
        monitored: true,
        upgradeUntilCustomFormatScore: 1500,
        seasons: [
          {
            number: 2,
            monitored: true,
            episodes: [
              {
                number: 3,
                title: 'Doors & Corners',
                customFormatScore: 900,
                airDateUtc: '2026-05-12T00:00:00Z',
              } as SonarrLibraryEpisodeTable,
            ],
          } as SonarrLibrarySeasonTable,
        ],
      } as SonarrLibrarySeriesTable,
    ],
  };

  protected getSeriesUrl(row: { url: string | null }): string | null {
    return row.url;
  }
}

describe('SonarrTableWidget', () => {
  let fixture: ComponentFixture<SonarrTableHostComponent>;
  let open: jasmine.Spy;

  beforeEach(async () => {
    open = jasmine.createSpy('open');
    await TestBed.configureTestingModule({
      imports: [SonarrTableHostComponent],
      providers: [{ provide: MatDialog, useValue: { open } }],
    }).compileComponents();

    fixture = TestBed.createComponent(SonarrTableHostComponent);
    fixture.detectChanges();
  });

  it('should render a series editor link', () => {
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('.text-link') as HTMLAnchorElement;

    expect(link.href).toBe('http://localhost:8989/series/the-expanse');
  });

  it('should render a collapse button in the table header', () => {
    const collapseButton: Element | null = (fixture.nativeElement as HTMLElement).querySelector('.card-header-actions button');

    expect(collapseButton).toBeTruthy();
  });

  it('opens the matching episode details without collapsing its series or season', async () => {
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLElement>('.series-row')!.click();
    await fixture.whenStable();
    element.querySelector<HTMLElement>('.detail-table .clickable-row')!.click();
    await fixture.whenStable();
    const row = element.querySelector<HTMLElement>('.episode-detail-row')!;
    row.click();
    expect(open).toHaveBeenCalledOnceWith(
      MediaDetailsDialogWidget,
      jasmine.objectContaining({
        data: jasmine.objectContaining({
          title: 'Doors & Corners',
          instanceName: 'Sonarr',
          seriesTitle: 'The Expanse',
          seasonNumber: 2,
          episodeNumber: 3,
          customFormatScore: 900,
          upgradeUntilCustomFormatScore: 1500,
          url: 'http://localhost:8989/series/the-expanse',
        }),
      }),
    );
    await fixture.whenStable();
    expect(element.querySelector('.series-row.is-expanded')).toBeTruthy();
    expect(element.querySelector('.episode-detail-row')).toBeTruthy();
    row.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    expect(open).toHaveBeenCalledTimes(2);
  });

  it('should mark summary rows as expanded when key is expanded', async () => {
    const row: HTMLElement | null = fixture.nativeElement.querySelector('.series-row');

    row?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const expandedSummaryRow: Element | null = (fixture.nativeElement as HTMLElement).querySelector('.series-row.is-expanded');
    const detailTable: Element | null = (fixture.nativeElement as HTMLElement).querySelector('.detail-table');

    expect(expandedSummaryRow).toBeTruthy();
    expect(detailTable).toBeTruthy();
  });
});

@Component({
  imports: [SonarrTableWidget, TableColumnTemplateDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-sonarr-table [table]="table">
      <ng-template appTableColumn [appTableColumnConfig]="{ kind: 'series', key: 'title', header: 'Serie', showExpandIcon: false }" let-row>
        {{ row.title }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'season', key: 'number', header: 'Season', showExpandIcon: false }"
        let-season
      >
        {{ season.number }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'episode', key: 'number', header: 'Folge', showExpandIcon: false }"
        let-episode
      >
        {{ episode.number }}
      </ng-template>
    </app-sonarr-table>
  `,
})
class SeasonOrderHostComponent {
  table: SonarrLibraryTable = {
    instanceName: 'Sonarr',
    instanceId: 'sonarr-1',
    syncedAt: '2026-05-30T21:42:04.0728462Z',
    series: [
      {
        url: '',
        id: 1,
        title: 'Order',
        titleSlug: 'order',
        affectedSeasons: 2,
        affectedEpisodes: 2,
        qualityProfileName: 'HD',
        status: 'continuing',
        monitored: true,
        upgradeUntilCustomFormatScore: null,
        seasons: [
          { number: 0, monitored: true, episodes: [{ number: 1, title: 'Special', customFormatScore: null, airDateUtc: null }] },
          { number: 1, monitored: true, episodes: [{ number: 1, title: 'Episode', customFormatScore: null, airDateUtc: null }] },
        ],
      } as unknown as SonarrLibrarySeriesTable,
    ],
  };
}

describe('SonarrTableWidget season ordering', () => {
  it('lists the specials season after the numbered seasons', async () => {
    await TestBed.configureTestingModule({
      imports: [SeasonOrderHostComponent],
      providers: [{ provide: MatDialog, useValue: { open: () => undefined } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(SeasonOrderHostComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLElement>('.series-row')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const seasonRows = Array.from(element.querySelectorAll('.detail-table .clickable-row')).map((row) => row.textContent!.match(/\d+/)![0]);
    expect(seasonRows).toEqual(['1', '0']);
  });
});
