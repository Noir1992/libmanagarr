import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ContentChildren,
  effect,
  inject,
  input,
  InputSignal,
  QueryList,
  Signal,
  signal,
  ViewChild,
  WritableSignal,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatTable, MatTableModule } from '@angular/material/table';
import { MatDialog } from '@angular/material/dialog';
import { episodeDetailData, isDetailRowActivation, specialsLast } from '../../utils/media-detail-utils';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';
import {
  SonarrTableEpisodeType,
  SonarrTableSeasonType,
  SonarrTableSeriesType,
  SonarrTableType,
} from '../../interfaces/arr-table/sonarr-table-data-type';
import { TableColumnTemplateKind } from '../../interfaces/general/table-column-template';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { TableColumnTemplateDirective } from '../table-column-template.directive';

@Component({
  selector: 'app-sonarr-table',
  imports: [MatTableModule, AppDatePipe, MatIconButton, NgTemplateOutlet],
  templateUrl: './sonarr-table.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './sonarr-table.widget.scss',
})
export class SonarrTableWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialog = inject(MatDialog);

  @ViewChild(MatTable)
  private readonly matTable?: MatTable<unknown>;
  @ContentChildren(TableColumnTemplateDirective<SonarrTableSeriesType>)
  private readonly projectedColumns?: QueryList<TableColumnTemplateDirective>;

  protected readonly updatedLabel: Signal<string> = computed((): string => this.i18n.t('table.updated'));
  protected getSeriesCountLabel(count: number): string {
    return this.i18n.tCount(count, 'common.seriesCountOne', 'common.seriesCount');
  }

  readonly table: InputSignal<SonarrTableType> = input.required<SonarrTableType>();

  protected readonly collapsed: WritableSignal<boolean> = signal<boolean>(false);
  private readonly expandedKeys: WritableSignal<string[]> = signal<string[]>([]);

  protected readonly trackBySeriesId = (_index: number, row: SonarrTableSeriesType): number => row.id;
  protected readonly detailColumn: readonly string[] = ['expandedDetail'];

  constructor() {
    effect(() => {
      const validKeys: Set<string> = this.collectValidKeys(this.table().series);
      this.expandedKeys.update((currentKeys: string[]) =>
        currentKeys.every((key: string) => validKeys.has(key)) ? currentKeys : currentKeys.filter((key: string) => validKeys.has(key)),
      );
    });
  }

  protected toggleCollapsed(): void {
    this.collapsed.update((collapsed) => !collapsed);
  }

  protected openEpisodeDetails(
    series: SonarrTableSeriesType,
    season: SonarrTableSeasonType,
    episode: SonarrTableEpisodeType,
    event: Event,
  ): void {
    if (!isDetailRowActivation(event)) return;
    this.dialog.open(MediaDetailsDialogWidget, {
      data: episodeDetailData(episode, season.number, series, this.table().instanceName),
      width: '700px',
      maxWidth: '95vw',
    });
  }

  protected isExpanded(key: string): boolean {
    return this.expandedKeys().includes(key);
  }

  protected toggleExpandedKey(key: string): void {
    this.expandedKeys.update((currentKeys: string[]) =>
      currentKeys.includes(key) ? currentKeys.filter((currentKey: string) => currentKey !== key) : [...currentKeys, key],
    );
    this.matTable?.renderRows();
  }

  protected readonly isDetailRow: (index: number, series: SonarrTableSeriesType) => boolean = (
    _index: number,
    series: SonarrTableSeriesType,
  ): boolean => this.isExpanded(series.id.toString());

  protected getSeasonSummaries(series: SonarrTableSeriesType): SonarrTableSeasonType[] {
    return specialsLast(series.seasons);
  }

  protected seriesColumns(): readonly TableColumnTemplateDirective<SonarrTableSeriesType>[] {
    return this.columnTemplatesFor('series') as readonly TableColumnTemplateDirective<SonarrTableSeriesType>[];
  }

  protected seasonColumns(): readonly TableColumnTemplateDirective<SonarrTableSeasonType>[] {
    return this.columnTemplatesFor('season') as readonly TableColumnTemplateDirective<SonarrTableSeasonType>[];
  }

  protected episodeColumns(): readonly TableColumnTemplateDirective<SonarrTableEpisodeType>[] {
    return this.columnTemplatesFor('episode') as readonly TableColumnTemplateDirective<SonarrTableEpisodeType>[];
  }

  protected seriesColumnIds(): readonly string[] {
    return this.seriesColumns().map((column) => column.appTableColumnConfig().key);
  }

  protected seasonColumnIds(): readonly string[] {
    return this.seasonColumns().map((column) => column.appTableColumnConfig().key);
  }

  private columnTemplatesFor(kind: TableColumnTemplateKind): readonly TableColumnTemplateDirective[] {
    return this.projectedColumns?.toArray().filter((column) => column.appTableColumnConfig().kind === kind) ?? [];
  }

  private collectValidKeys(series: readonly SonarrTableSeriesType[]): Set<string> {
    const keys: Set<string> = new Set<string>();

    for (const singleSeries of series) {
      const seriesKey: string = singleSeries.id.toString();
      keys.add(seriesKey);

      for (const season of singleSeries.seasons) {
        const seasonKey: string = seriesKey + '-' + season.number;
        keys.add(seasonKey);

        for (const episode of season.episodes) {
          keys.add(seasonKey + '-' + episode.number);
        }
      }
    }

    return keys;
  }
}
