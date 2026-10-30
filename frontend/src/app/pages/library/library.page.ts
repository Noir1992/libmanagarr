import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  linkedSignal,
  OnDestroy,
  OnInit,
  signal,
  untracked,
  Signal,
  WritableSignal,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { firstValueFrom, forkJoin, Observable, Subject, Subscription, takeUntil } from 'rxjs';
import { AiredStatus } from '../../enums/aired-status';
import { MonitoredStatus } from '../../enums/monitored-status';
import { LibrarySortDirection } from '../../enums/library-sort-direction';
import { LibrarySortField } from '../../enums/library-sort-field';
import { DEFAULT_FILTER_ALL, FilterMultiSelectOption, FilterOption } from '../../interfaces/filter-option';
import { FilterOptionType } from '../../enums/filter-option-type';
import { MediaGridDetailTone, MediaGridItem } from '../../interfaces/general/media-grid-item';
import { TableColumnTemplateDefinition } from '../../interfaces/general/table-column-template';
import { LibraryFilterCounts, LibraryFilterValues } from '../../interfaces/library/library-filter-values';
import { LibraryRequestFilter } from '../../interfaces/library/library-request-filter';
import { RadarrLibraryBatchResponse, RadarrLibraryInstanceResult } from '../../interfaces/library/radarr-library-batch-response';
import { RadarrLibraryEntryResponse } from '../../interfaces/library/radarr-library-entry-response';
import { RadarrLibraryMovieTable } from '../../interfaces/library/radarr-library-movie-table';
import { RadarrLibraryTable } from '../../interfaces/library/radarr-library-table';
import { SonarrLibraryBatchResponse, SonarrLibraryInstanceResult } from '../../interfaces/library/sonarr-library-batch-response';
import { SonarrLibraryEntryResponse } from '../../interfaces/library/sonarr-library-entry-response';
import { SonarrLibrarySeriesTable } from '../../interfaces/library/sonarr-library-series-table';
import { SonarrLibraryTable } from '../../interfaces/library/sonarr-library-table';
import { ArrInstanceSettings } from '../../interfaces/settings/arr-instance-settings';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { normalizeBaseUrl } from '../../utils/base-utils';
import { episodeDetailData, movieDetailData } from '../../utils/media-detail-utils';
import { airStatusTone, fileStatusTone } from '../../utils/media-status-tone.util';
import { normalizeResolutionGroup, RESOLUTION_GROUPS } from '../../utils/resolution-statistics-utils';
import { dateRangeFilter, multiSelectFilterValues, numberRangeFilter, selectFilterValue } from '../../utils/filter-utils';
import { latestAirDate, LibrarySortKey, sortLibraryEntries } from '../../utils/library-sort-utils';
import { MediaGridWidget } from '../../widgets/media-grid/media-grid.widget';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { RadarrTableWidget } from '../../widgets/radarr-table/radarr-table.widget';
import { SortControlWidget } from '../../widgets/sort-control/sort-control.widget';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { NoArrInstancesConfiguredSectionWidget } from '../../widgets/sections/no-arr-instances-configured-section/no-arr-instances-configured-section.widget';
import { NoDataSectionWidget } from '../../widgets/sections/no-data-section/no-data-section.widget';
import { SonarrTableWidget } from '../../widgets/sonarr-table/sonarr-table.widget';
import { TableColumnTemplateDirective } from '../../widgets/table-column-template.directive';

@Component({
  selector: 'app-library-page',
  imports: [
    MatIconButton,
    MatIconModule,
    MatInput,
    MediaGridWidget,
    SonarrTableWidget,
    RadarrTableWidget,
    PageFilterWidget,
    SortControlWidget,
    TableColumnTemplateDirective,
    AppDatePipe,
    NoArrInstancesConfiguredSectionWidget,
    LoadingErrorSectionWidget,
    LoadingSectionWidget,
    NoDataSectionWidget,
  ],
  templateUrl: './library.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './library.page.scss',
})
export class LibraryPage implements OnInit, OnDestroy {
  private static readonly VIEW_MODE_KEY: string = 'viewMode:library';
  private static readonly SORT_KEY: string = 'sort:library';

  /* Injections */
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly sonarrApi: SonarrApiService = inject(SonarrApiService);
  private readonly radarrApi: RadarrApiService = inject(RadarrApiService);

  /* I18n signals */
  protected readonly title: Signal<string> = computed((): string => this.i18n.t('library.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('library.description'));
  protected readonly searchLabel: Signal<string> = computed((): string => this.i18n.t('library.search'));
  protected readonly naLabel: Signal<string> = computed((): string => this.i18n.t('common.na'));
  protected readonly viewToggleLabel: Signal<string> = computed((): string => this.i18n.t('view.toggle'));
  protected readonly viewListLabel: Signal<string> = computed((): string => this.i18n.t('view.list'));
  protected readonly viewGridLabel: Signal<string> = computed((): string => this.i18n.t('view.grid'));
  protected readonly sonarrSeriesColumns: Signal<TableColumnTemplateDefinition[]> = computed(() => [
    { kind: 'series', key: 'title', header: this.i18n.t('sonarr.column.series'), showExpandIcon: true },
    { kind: 'series', key: 'affectedSeasons', header: this.i18n.t('common.column.season'), showExpandIcon: false },
    { kind: 'series', key: 'affectedEpisodes', header: this.i18n.t('common.column.episode'), showExpandIcon: false },
    { kind: 'series', key: 'status', header: this.i18n.t('filter.airStatus'), showExpandIcon: false },
    { kind: 'series', key: 'qualityProfileName', header: this.i18n.t('common.column.profile'), showExpandIcon: false },
    { kind: 'series', key: 'upgradeUntilCustomFormatScore', header: this.i18n.t('common.column.targetScore'), showExpandIcon: false },
  ]);
  protected readonly sonarrSeasonColumns: Signal<TableColumnTemplateDefinition[]> = computed(() => [
    { kind: 'season', key: 'number', header: this.i18n.t('common.column.season'), showExpandIcon: false },
  ]);
  protected readonly sonarrEpisodeColumns: Signal<TableColumnTemplateDefinition[]> = computed(() => [
    { kind: 'episode', key: 'number', header: this.i18n.t('common.column.episode'), showExpandIcon: false },
    { kind: 'episode', key: 'title', header: this.i18n.t('common.column.title'), showExpandIcon: false },
    { kind: 'episode', key: 'airDateUtc', header: this.i18n.t('common.column.airDate'), showExpandIcon: false },
    { kind: 'episode', key: 'customFormatScore', header: this.i18n.t('common.column.score'), showExpandIcon: false },
    { kind: 'episode', key: 'hasFile', header: this.i18n.t('filter.fileStatus'), showExpandIcon: false },
    { kind: 'episode', key: 'monitored', header: this.i18n.t('filter.episodeMonitored'), showExpandIcon: false },
  ]);
  protected readonly radarrColumns: Signal<TableColumnTemplateDefinition[]> = computed(() => [
    { kind: 'movie', key: 'title', header: this.i18n.t('common.column.movie'), showExpandIcon: false },
    { kind: 'movie', key: 'airDateUtc', header: this.i18n.t('common.column.airDate'), showExpandIcon: false },
    { kind: 'movie', key: 'status', header: this.i18n.t('filter.airStatus'), showExpandIcon: false },
    { kind: 'movie', key: 'qualityProfileName', header: this.i18n.t('common.column.profile'), showExpandIcon: false },
    { kind: 'movie', key: 'upgradeUntilCustomFormatScore', header: this.i18n.t('common.column.targetScore'), showExpandIcon: false },
    { kind: 'movie', key: 'customFormatScore', header: this.i18n.t('common.column.score'), showExpandIcon: false },
    { kind: 'movie', key: 'hasFile', header: this.i18n.t('filter.fileStatus'), showExpandIcon: false },
  ]);

  private loadSequence = 0;
  private readonly filteredCounts = signal<LibraryFilterCounts | null>(null);

  /* Signals */
  protected readonly sonarrInstances: Signal<readonly ArrInstanceSettings[]> = computed((): readonly ArrInstanceSettings[] =>
    this.appSettingsService.sonarrInstances(),
  );
  protected readonly radarrInstances: Signal<readonly ArrInstanceSettings[]> = computed((): readonly ArrInstanceSettings[] =>
    this.appSettingsService.radarrInstances(),
  );
  protected readonly loadSettingsError: Signal<string | null> = this.appSettingsService.loadError;
  protected readonly loading: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly initialLoadComplete: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly loadError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly titleSearch: WritableSignal<string> = signal<string>('');
  private readonly titleSearchQuery: Signal<string> = computed((): string => this.titleSearch().trim().toLocaleLowerCase());
  protected readonly activeFilterCount: WritableSignal<number> = signal<number>(0);
  protected readonly isFiltered: Signal<boolean> = computed(
    (): boolean => this.activeFilterCount() > 0 || this.titleSearchQuery().length > 0,
  );
  protected readonly viewMode: WritableSignal<'table' | 'grid'> = signal<'table' | 'grid'>('grid');
  protected readonly sortField: WritableSignal<LibrarySortField> = signal<LibrarySortField>(LibrarySortField.ALPHABETICAL);
  protected readonly sortDirection: WritableSignal<LibrarySortDirection> = signal<LibrarySortDirection>(LibrarySortDirection.ASCENDING);
  protected readonly sonarrInstanceTables: WritableSignal<SonarrLibraryTable[]> = signal<SonarrLibraryTable[]>([]);
  protected readonly radarrInstanceTables: WritableSignal<RadarrLibraryTable[]> = signal<RadarrLibraryTable[]>([]);
  // Client-side title search runs on the already loaded tables only (no backend reload while typing).
  protected readonly visibleRadarrTables: Signal<RadarrLibraryTable[]> = computed((): RadarrLibraryTable[] => {
    const query: string = this.titleSearchQuery();
    if (query.length === 0) {
      return this.radarrInstanceTables();
    }

    return this.radarrInstanceTables()
      .map((table) => ({ ...table, movies: table.movies.filter((movie) => movie.title.toLocaleLowerCase().includes(query)) }))
      .filter((table) => table.movies.length > 0);
  });
  protected readonly visibleSonarrTables: Signal<SonarrLibraryTable[]> = computed((): SonarrLibraryTable[] => {
    const query: string = this.titleSearchQuery();
    if (query.length === 0) {
      return this.sonarrInstanceTables();
    }

    return this.sonarrInstanceTables()
      .map((table) => ({ ...table, series: table.series.filter((series) => series.title.toLocaleLowerCase().includes(query)) }))
      .filter((table) => table.series.length > 0);
  });
  protected readonly noInstancesConfigured: Signal<boolean> = computed(
    (): boolean => this.sonarrInstances().length === 0 && this.radarrInstances().length === 0,
  );
  protected readonly noDataAvailable: Signal<boolean> = computed((): boolean => {
    return (
      !this.visibleRadarrTables().some((table) => table.movies.length > 0) &&
      !this.visibleSonarrTables().some((table) => table.series.length > 0)
    );
  });
  protected readonly visibleItemCount: Signal<number> = computed((): number => {
    const movieCount: number = this.visibleRadarrTables().reduce((count, table) => count + table.movies.length, 0);
    const seriesCount: number = this.visibleSonarrTables().reduce((count, table) => count + table.series.length, 0);

    return movieCount + seriesCount;
  });
  protected readonly gridItems: Signal<MediaGridItem[]> = computed(() => {
    const items: MediaGridItem[] = [];

    for (const table of this.visibleRadarrTables()) {
      for (const movie of table.movies) {
        const posterUrl: string | null = `/api/radarr/instances/${table.instanceId}/poster/${movie.id}?w=342`;
        items.push({
          kind: 'movie',
          id: movie.id,
          title: movie.title,
          size: movie.size,
          url: movie.url,
          posterUrl,
          movieData: { ...movieDetailData(movie, table.instanceName), posterUrl },
          detailRows: [
            { label: this.i18n.t('common.column.instance'), value: table.instanceName },
            { label: this.i18n.t('filter.airStatus'), value: movie.status, tone: airStatusTone(movie.status) },
            {
              label: this.i18n.t('filter.fileStatus'),
              value: this.fileStatusLabel(movie.hasFile),
              tone: fileStatusTone(movie.hasFile),
            },
            { label: this.i18n.t('filter.monitored'), value: this.monitoredLabel(movie.monitored) },
          ],
        });
      }
    }

    for (const table of this.visibleSonarrTables()) {
      for (const series of table.series) {
        const posterUrl: string | null = `/api/sonarr/instances/${table.instanceId}/poster/${series.id}?w=342`;
        items.push({
          kind: 'series',
          id: series.id,
          title: series.title,
          size: series.totalSize,
          url: series.url,
          posterUrl,
          seriesData: {
            seasons: series.seasons.map((s) => ({
              number: s.number,
              episodes: s.episodes.map((e) => ({
                details: episodeDetailData(e, s.number, series, table.instanceName),
                number: e.number,
                title: e.title,
                customFormatScore: e.customFormatScore,
                hasFile: e.hasFile,
                monitored: e.monitored,
                airDateUtc: e.airDateUtc,
              })),
            })),
            qualityProfileName: series.qualityProfileName,
            upgradeUntilCustomFormatScore: series.upgradeUntilCustomFormatScore,
            instanceName: table.instanceName,
            status: series.status,
            addedAt: series.addedAt,
            isAvailable: this.seriesIsAvailable(series),
            monitored: series.monitored,
            genres: series.genres,
            tags: series.tags,
          },
          detailRows: [
            { label: this.i18n.t('common.column.instance'), value: table.instanceName },
            { label: this.i18n.t('filter.airStatus'), value: series.status, tone: airStatusTone(series.status) },
            {
              label: this.i18n.t('filter.fileStatus'),
              value: this.seriesFileStatusLabel(series),
              tone: this.seriesFileStatusTone(series),
            },
            { label: this.i18n.t('filter.seriesMonitored'), value: this.monitoredLabel(series.monitored) },
          ],
        });
      }
    }

    return sortLibraryEntries(
      items,
      (item: MediaGridItem): LibrarySortKey => ({
        title: item.title,
        size: item.size,
        airedAt: item.kind === 'movie' ? (item.movieData?.airDateUtc ?? null) : latestSeriesAirDate(item),
        addedAt: item.kind === 'movie' ? (item.movieData?.addedAt ?? null) : (item.seriesData?.addedAt ?? null),
      }),
      this.sortField(),
      this.sortDirection(),
      this.appSettingsService.titleSorting(),
    );
  });
  protected readonly arrInstanceFilters: WritableSignal<LibraryFilterValues> = signal<LibraryFilterValues>({
    genres: [],
    tags: [],
    statuses: [],
    qualityProfiles: [],
    audioLanguages: [],
    subtitleLanguages: [],
    resolutions: [],
    videoCodecs: [],
    audioCodecs: [],
    dynamicRanges: [],
    containerFormats: [],
  });
  private readonly filterDefinitions: Signal<readonly FilterOption[]> = computed((): readonly FilterOption[] => {
    const filters: FilterOption[] = [];

    const allLabel: string = this.i18n.t('filter.all');

    filters.push(
      {
        id: 'aired-filter',
        label: this.i18n.t('filter.aired'),
        type: FilterOptionType.SELECT,
        options: [
          { label: allLabel, value: AiredStatus.ALL },
          { label: this.i18n.t('filter.airedYes'), value: AiredStatus.AIRED },
          { label: this.i18n.t('filter.airedNo'), value: AiredStatus.NOT_AIRED },
        ],
        selectedValue: AiredStatus.ALL,
        onChanged: () => this.loadData(),
      },
      {
        id: 'monitored-filter',
        label: this.i18n.t('filter.seriesMonitored'),
        type: FilterOptionType.SELECT,
        options: [
          { label: allLabel, value: MonitoredStatus.ALL },
          { label: this.i18n.t('filter.monitoredYes'), value: MonitoredStatus.MONITORED },
          { label: this.i18n.t('filter.monitoredNo'), value: MonitoredStatus.NOT_MONITORED },
        ],
        selectedValue: MonitoredStatus.ALL,
        onChanged: () => this.loadData(),
      },
      {
        id: 'upgrade-target-filter',
        label: this.i18n.t('filter.upgradeTarget'),
        type: FilterOptionType.SELECT,
        options: [
          { label: allLabel, value: 'all' },
          { label: this.i18n.t('filter.upgradeTargetBelow'), value: 'below' },
          { label: this.i18n.t('filter.upgradeTargetReached'), value: 'reached' },
        ],
        selectedValue: 'all',
        onChanged: () => this.loadData(),
      },
      {
        id: 'file-status-filter',
        label: this.i18n.t('filter.fileStatus'),
        type: FilterOptionType.SELECT,
        options: [
          { label: allLabel, value: 'all' },
          { label: this.i18n.t('filter.filePresent'), value: 'present' },
          { label: this.i18n.t('filter.fileMissing'), value: 'missing' },
        ],
        selectedValue: 'all',
        onChanged: () => this.loadData(),
      },
      {
        id: 'episode-monitored-filter',
        label: this.i18n.t('filter.episodeMonitored'),
        type: FilterOptionType.SELECT,
        options: [
          { label: allLabel, value: 'all' },
          { label: this.i18n.t('filter.monitoredYes'), value: 'monitored' },
          { label: this.i18n.t('filter.monitoredNo'), value: 'notMonitored' },
        ],
        selectedValue: 'all',
        onChanged: () => this.loadData(),
      },
      {
        id: 'episode-type-filter',
        label: this.i18n.t('filter.episodeType'),
        type: FilterOptionType.MULTISELECT,
        options: [
          { label: this.i18n.t('filter.movie'), value: 'movie' },
          { label: this.i18n.t('filter.episodeRegular'), value: 'regular' },
          { label: this.i18n.t('filter.episodeSpecial'), value: 'special' },
        ],
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'status-filter',
        label: this.i18n.t('filter.airStatus'),
        type: FilterOptionType.MULTISELECT,
        options: this.arrInstanceFilters().statuses.map((s) => ({ value: s, label: s })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'genre-filter',
        label: this.i18n.t('filter.genres'),
        type: FilterOptionType.MULTISELECT,
        options: this.arrInstanceFilters().genres.map((g) => ({ value: g, label: g })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'tag-filter',
        label: this.i18n.t('filter.tags'),
        type: FilterOptionType.MULTISELECT,
        options: this.arrInstanceFilters().tags.map((t) => ({ value: t, label: t })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'profile-filter',
        label: this.i18n.t('filter.profile'),
        type: FilterOptionType.MULTISELECT,
        options: this.arrInstanceFilters().qualityProfiles.map((p) => ({ value: p, label: p })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'audio-language-filter',
        label: this.i18n.t('filter.audioLanguages'),
        type: FilterOptionType.MULTISELECT,
        options: this.arrInstanceFilters().audioLanguages.map((l) => ({ value: l, label: l })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'subtitle-language-filter',
        label: this.i18n.t('filter.subtitleLanguages'),
        type: FilterOptionType.MULTISELECT,
        options: this.arrInstanceFilters().subtitleLanguages.map((l) => ({ value: l, label: l })),
        selectedValues: [],
        onChanged: () => {
          this.loadData();
        },
      },
      {
        id: 'resolution-filter',
        label: this.i18n.t('filter.resolution'),
        type: FilterOptionType.MULTISELECT,
        options: RESOLUTION_GROUPS.filter((group) =>
          (this.arrInstanceFilters().resolutions ?? []).some((value) => normalizeResolutionGroup(value) === group),
        ).map((value) => ({ value, label: value })),
        normalizeValue: (value) => (value === DEFAULT_FILTER_ALL ? value : normalizeResolutionGroup(value)),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'video-codec-filter',
        label: this.i18n.t('filter.videoCodec'),
        type: FilterOptionType.MULTISELECT,
        options: (this.arrInstanceFilters().videoCodecs ?? []).map((value) => ({
          value,
          label: value === 'Unknown' ? this.i18n.t('filter.unknown') : value,
        })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'audio-codec-filter',
        label: this.i18n.t('filter.audioCodec'),
        type: FilterOptionType.MULTISELECT,
        options: (this.arrInstanceFilters().audioCodecs ?? []).map((value) => ({
          value,
          label: value === 'Unknown' ? this.i18n.t('filter.unknown') : value,
        })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'dynamic-range-filter',
        label: this.i18n.t('filter.dynamicRange'),
        type: FilterOptionType.MULTISELECT,
        options: (this.arrInstanceFilters().dynamicRanges ?? []).map((value) => ({
          value,
          label: value === 'Unknown' ? this.i18n.t('filter.unknown') : value,
        })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'container-format-filter',
        label: this.i18n.t('filter.containerFormat'),
        type: FilterOptionType.MULTISELECT,
        options: (this.arrInstanceFilters().containerFormats ?? []).map((value) => ({
          value,
          label: value === 'Unknown' ? this.i18n.t('filter.unknown') : value,
        })),
        selectedValues: [],
        onChanged: () => this.loadData(),
      },
      {
        id: 'video-bitrate-range',
        label: this.i18n.t('filter.videoBitrate'),
        type: FilterOptionType.NUMBER_RANGE,
        minValue: null,
        maxValue: null,
        minimum: 0,
        step: 'any',
        onChanged: () => this.scheduleLoadData(),
      },
      {
        id: 'score-range',
        label: this.i18n.t('filter.score'),
        type: FilterOptionType.NUMBER_RANGE,
        minValue: null,
        maxValue: null,
        onChanged: () => this.scheduleLoadData(),
      },
      {
        id: 'added-range',
        label: this.i18n.t('filter.addedRange'),
        type: FilterOptionType.DATE_RANGE,
        startDate: null,
        endDate: null,
        onChanged: () => this.scheduleLoadData(),
      },
      {
        id: 'aired-range',
        label: this.i18n.t('filter.airedRange'),
        type: FilterOptionType.DATE_RANGE,
        startDate: null,
        endDate: null,
        onChanged: () => this.scheduleLoadData(),
      },
    );

    if (this.radarrInstances().length > 0) {
      const radarrOptions: FilterMultiSelectOption[] = this.radarrInstances().map((instance) => ({
        value: instance.id,
        label: instance.name,
      }));

      filters.push({
        id: 'radarr-instance-filter',
        label: this.i18n.t('filter.radarrInstances'),
        type: FilterOptionType.MULTISELECT,
        options: radarrOptions,
        selectedValues: [],
        excludeAllRestricts: true,
        onChanged: () =>
          this.loadFilterValues().then((updated) => {
            if (updated) this.loadData();
          }),
      });
    }

    if (this.sonarrInstances().length > 0) {
      const sonarrOptions: FilterMultiSelectOption[] = this.sonarrInstances().map((instance) => ({
        value: instance.id,
        label: instance.name,
      }));

      filters.push({
        id: 'sonarr-instance-filter',
        label: this.i18n.t('filter.sonarrInstances'),
        type: FilterOptionType.MULTISELECT,
        options: sonarrOptions,
        selectedValues: [],
        excludeAllRestricts: true,
        onChanged: () =>
          this.loadFilterValues().then((updated) => {
            if (updated) this.loadData();
          }),
      });
    }

    return filters
      .filter((filter) => this.sonarrInstances().length > 0 || filter.id !== 'monitored-filter')
      .map((filter) => {
        if (filter.type === FilterOptionType.MULTISELECT) {
          return {
            ...filter,
            supportsExclusion: true,
            totalCount: this.filterValueCount(filter.id, DEFAULT_FILTER_ALL),
            options: filter.options.map((option) => ({ ...option, count: this.filterValueCount(filter.id, option.value) })),
          };
        }
        if (filter.type === FilterOptionType.SELECT) {
          return {
            ...filter,
            supportsExclusion: true,
            options: filter.options.map((option) => ({
              ...option,
              count: this.filterValueCount(filter.id, option.value === 'all' ? DEFAULT_FILTER_ALL : option.value),
            })),
          };
        }
        return { ...filter, supportsExclusion: true };
      });
  });

  protected readonly filterOptions: Signal<readonly FilterOption[]> = linkedSignal<readonly FilterOption[], readonly FilterOption[]>({
    source: this.filterDefinitions,
    computation: (filters, previous) =>
      filters.map((filter) => {
        const selected: FilterOption | undefined = previous?.value.find((option) => option.id === filter.id);
        filter = { ...filter, exclude: selected?.exclude ?? false };
        if (filter.type === FilterOptionType.MULTISELECT && selected?.type === FilterOptionType.MULTISELECT) {
          const normalizeValue = filter.normalizeValue;
          return { ...filter, selectedValues: [...new Set(selected.selectedValues.map((value) => normalizeValue?.(value) ?? value))] };
        }
        if (filter.type === FilterOptionType.SELECT && selected?.type === FilterOptionType.SELECT) {
          return { ...filter, selectedValue: selected.selectedValue };
        }
        if (filter.type === FilterOptionType.NUMBER_RANGE && selected?.type === FilterOptionType.NUMBER_RANGE) {
          return { ...filter, minValue: selected.minValue, maxValue: selected.maxValue };
        }
        if (filter.type === FilterOptionType.DATE_RANGE && selected?.type === FilterOptionType.DATE_RANGE) {
          return { ...filter, startDate: selected.startDate, endDate: selected.endDate };
        }
        return filter;
      }),
  });

  /* Lifecycle */
  private activeRequest?: Subscription;
  private debounceTimer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  private filterValuesSequence = 0;
  private readonly cancelFilterValues = new Subject<void>();

  constructor() {
    let previousRules = JSON.stringify(this.appSettingsService.titleSorting());
    effect(() => {
      const rules = JSON.stringify(this.appSettingsService.titleSorting());
      if (rules === previousRules) return;
      previousRules = rules;
      // Reload through the same cancellation/sequence path used by filter and sort changes.
      // Before the first load, initialization will already request the current persisted rules.
      untracked(() => {
        if (!this.destroyed && this.loadSequence > 0) this.loadData();
      });
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.cancelFilterValues.next();
    this.cancelFilterValues.complete();
    this.loadSequence++;
    this.activeRequest?.unsubscribe();
    clearTimeout(this.debounceTimer);
  }

  ngOnInit(): void {
    this.restoreViewMode();
    this.restoreSort();
    void this.initializeSettingsAsync();
  }

  private async initializeSettingsAsync(): Promise<void> {
    this.loading.set(true);

    try {
      await this.appSettingsService.loadInstances();
    } catch (error: unknown) {
      this.handelLoadError(error);
      return;
    }

    if (this.noInstancesConfigured()) {
      this.loading.set(false);
      return;
    }

    this.loadFilterValues().then((updated) => {
      if (updated) this.loadData();
    });
  }

  private async loadFilterValues(): Promise<boolean> {
    if (this.destroyed) return false;
    clearTimeout(this.debounceTimer);
    const sequence = ++this.filterValuesSequence;
    this.cancelFilterValues.next();
    this.loadSequence++;
    this.activeRequest?.unsubscribe();
    this.loading.set(true);

    const calls: Observable<LibraryFilterValues>[] = [];

    const radarrIds: string[] = this.effectiveInstanceFilterValues('radarr-instance-filter');
    if (radarrIds.length > 0) {
      calls.push(this.radarrApi.fetchFilterValues(radarrIds));
    }

    const sonarrIds: string[] = this.effectiveInstanceFilterValues('sonarr-instance-filter');
    if (sonarrIds.length > 0) {
      calls.push(this.sonarrApi.fetchFilterValues(sonarrIds));
    }

    try {
      const responses: LibraryFilterValues[] =
        calls.length > 0 ? await firstValueFrom(forkJoin(calls).pipe(takeUntil(this.cancelFilterValues)), { defaultValue: [] }) : [];
      if (this.destroyed || sequence !== this.filterValuesSequence) return false;
      const mergedFilterValues: LibraryFilterValues = responses.reduce(
        (merged: LibraryFilterValues, response: LibraryFilterValues): LibraryFilterValues => ({
          counts: this.mergeFilterCounts(merged.counts, response.counts),
          genres: this.mergeFilterValues(merged.genres, response.genres),
          tags: this.mergeFilterValues(merged.tags, response.tags),
          statuses: this.mergeFilterValues(merged.statuses, response.statuses),
          qualityProfiles: this.mergeFilterValues(merged.qualityProfiles, response.qualityProfiles),
          audioLanguages: this.mergeFilterValues(merged.audioLanguages, response.audioLanguages),
          subtitleLanguages: this.mergeFilterValues(merged.subtitleLanguages, response.subtitleLanguages),
          resolutions: this.mergeFilterValues(merged.resolutions, response.resolutions),
          videoCodecs: this.mergeFilterValues(merged.videoCodecs, response.videoCodecs),
          audioCodecs: this.mergeFilterValues(merged.audioCodecs, response.audioCodecs),
          dynamicRanges: this.mergeFilterValues(merged.dynamicRanges, response.dynamicRanges),
          containerFormats: this.mergeFilterValues(merged.containerFormats, response.containerFormats),
        }),
        {
          genres: [],
          tags: [],
          statuses: [],
          qualityProfiles: [],
          audioLanguages: [],
          subtitleLanguages: [],
          resolutions: [],
          videoCodecs: [],
          audioCodecs: [],
          dynamicRanges: [],
          containerFormats: [],
        },
      );

      this.arrInstanceFilters.set(mergedFilterValues);
      return true;
    } catch (error: unknown) {
      if (!this.destroyed && sequence === this.filterValuesSequence) this.handelLoadError(error);
      return false;
    }
  }

  /* Handlers */
  protected onTitleSearchChanged(event: Event): void {
    this.titleSearch.set((event.target as HTMLInputElement).value);
  }

  protected toggleViewMode(): void {
    const nextMode: 'table' | 'grid' = this.viewMode() === 'table' ? 'grid' : 'table';
    this.viewMode.set(nextMode);
    this.persistViewMode(nextMode);
  }

  protected onSortFieldChanged(field: LibrarySortField): void {
    this.sortField.set(field);
    this.persistSort();
    this.scheduleLoadData();
  }

  protected onSortDirectionChanged(direction: LibrarySortDirection): void {
    this.sortDirection.set(direction);
    this.persistSort();
    this.scheduleLoadData();
  }

  /* Helpers */
  private upsertRadarrInstanceTable(instanceResult: RadarrLibraryInstanceResult): void {
    const instance: ArrInstanceSettings | undefined = this.appSettingsService
      .radarrInstances()
      .find((i) => i.id === instanceResult.instanceId);

    this.radarrInstanceTables.update((tables) => {
      const existingIndex: number = tables.findIndex((table) => table.instanceId === instanceResult.instanceId);
      const updatedTable: RadarrLibraryTable = {
        instanceId: instanceResult.instanceId,
        instanceName: instanceResult.instanceName,
        syncedAt: null,
        movies: this.buildMovieRows(instanceResult.rows, instance),
      };

      if (existingIndex === -1) {
        return [...tables, updatedTable];
      }

      return tables.map((table, index) => (index === existingIndex ? updatedTable : table));
    });
  }

  private buildMovieRows(rows: RadarrLibraryEntryResponse[], instance: ArrInstanceSettings | undefined): RadarrLibraryMovieTable[] {
    return rows.map((row) => ({
      url: instance ? `${normalizeBaseUrl(instance.baseUrl)}/movie/${row.movieTitleSlug}` : '',
      id: row.movieId,
      title: row.movieTitle,
      titleSlug: row.movieTitleSlug,
      qualityProfileName: row.qualityProfileName,
      upgradeUntilCustomFormatScore: row.upgradeUntilCustomFormatScore ?? null,
      genres: row.genres,
      tags: row.tags,
      status: row.status,
      addedAt: row.addedAt,
      airDateUtc: row.airDateUtc,
      monitored: row.monitored,
      hasFile: row.hasFile,
      customFormatScore: row.customFormatScore,
      hasAired: row.hasAired,
      mediaDetails: row.mediaDetails,
      resolution: row.resolution,
      size: row.size,
    }));
  }

  private upsertSonarrInstanceTable(instanceResult: SonarrLibraryInstanceResult): void {
    const instance: ArrInstanceSettings | undefined = this.appSettingsService
      .sonarrInstances()
      .find((i) => i.id === instanceResult.instanceId);

    this.sonarrInstanceTables.update((tables) => {
      const existingIndex: number = tables.findIndex((table) => table.instanceId === instanceResult.instanceId);
      const updatedTable: SonarrLibraryTable = {
        instanceId: instanceResult.instanceId,
        instanceName: instanceResult.instanceName,
        syncedAt: null,
        series: this.buildSeriesRows(instanceResult.rows, instance),
      };

      if (existingIndex === -1) {
        return [...tables, updatedTable];
      }

      return tables.map((table, index) => (index === existingIndex ? updatedTable : table));
    });
  }

  private buildSeriesRows(rows: SonarrLibraryEntryResponse[], instance: ArrInstanceSettings | undefined): SonarrLibrarySeriesTable[] {
    return rows.map((row) => ({
      url: instance ? `${normalizeBaseUrl(instance.baseUrl)}/series/${row.seriesTitleSlug}` : '',
      id: row.seriesId,
      title: row.seriesTitle,
      titleSlug: row.seriesTitleSlug,
      qualityProfileName: row.qualityProfileName,
      upgradeUntilCustomFormatScore: row.upgradeUntilCustomFormatScore ?? null,
      genres: row.genres,
      tags: row.tags,
      status: row.status,
      addedAt: row.addedAt,
      totalSize: row.totalSize,
      monitored: row.monitored,
      isAvailable: row.isAvailable,
      libraryEpisodeCount: row.libraryEpisodeCount,
      fileCount: row.fileCount,
      seasons: row.seasons.map((s) => ({
        number: s.number,
        monitored: s.monitored,
        episodes: s.episodes.map((e) => ({
          episodeId: e.episodeId,
          hasAired: e.hasAired,
          mediaDetails: e.mediaDetails,
          number: e.number,
          title: e.title,
          customFormatScore: e.customFormatScore,
          hasFile: e.hasFile,
          monitored: e.monitored,
          airDateUtc: e.airDateUtc,
        })),
      })),
      affectedSeasons: row.seasons.length,
      affectedEpisodes: row.totalEpisodes,
    }));
  }

  private scheduleLoadData(): void {
    this.loadSequence++;
    this.activeRequest?.unsubscribe();
    clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => this.loadData(), 200);
  }

  private loadData(): void {
    if (this.destroyed) return;
    clearTimeout(this.debounceTimer);
    this.activeRequest?.unsubscribe();
    const sequence = ++this.loadSequence;
    this.prepareLoadState();

    const filter: LibraryRequestFilter = this.buildLibraryRequestFilters();
    const calls: { [key: string]: Observable<RadarrLibraryBatchResponse | SonarrLibraryBatchResponse> } = {};

    if (this.effectiveInstanceFilterValues('radarr-instance-filter').length > 0) {
      calls['radarr'] = this.radarrApi.fetchLibrary(filter);
    }

    if (this.effectiveInstanceFilterValues('sonarr-instance-filter').length > 0) {
      calls['sonarr'] = this.sonarrApi.fetchLibrary(filter);
    }

    if (Object.keys(calls).length === 0) {
      this.filteredCounts.set({});
      this.loading.set(false);
      this.initialLoadComplete.set(true);
      return;
    }

    this.activeRequest = forkJoin(calls).subscribe({
      next: (response: { [key: string]: RadarrLibraryBatchResponse | SonarrLibraryBatchResponse }) => {
        if (sequence !== this.loadSequence) return;
        if (response['radarr']) {
          for (const instanceResult of (response['radarr'] as RadarrLibraryBatchResponse).instances) {
            this.upsertRadarrInstanceTable(instanceResult);
          }
        }

        if (response['sonarr']) {
          for (const instanceResult of (response['sonarr'] as SonarrLibraryBatchResponse).instances) {
            this.upsertSonarrInstanceTable(instanceResult);
          }
        }

        this.filteredCounts.set(
          Object.values(response).reduce((counts, result) => this.mergeFilterCounts(counts, result.counts), {} as LibraryFilterCounts),
        );
        this.loading.set(false);
        this.initialLoadComplete.set(true);
      },
      error: (error: HttpErrorResponse) => {
        if (sequence === this.loadSequence) this.handelLoadError(error);
      },
    });
  }

  private prepareLoadState(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.sonarrInstanceTables.set([]);
    this.radarrInstanceTables.set([]);
  }

  private restoreViewMode(): void {
    const stored: string | null = globalThis.localStorage.getItem(LibraryPage.VIEW_MODE_KEY);
    if (stored === 'grid' || stored === 'table') {
      this.viewMode.set(stored);
    }
  }

  private persistViewMode(mode: 'table' | 'grid'): void {
    globalThis.localStorage.setItem(LibraryPage.VIEW_MODE_KEY, mode);
  }

  private restoreSort(): void {
    const [field, direction]: (string | undefined)[] = (globalThis.localStorage.getItem(LibraryPage.SORT_KEY) ?? '').split(':');

    if (isLibrarySortField(field)) {
      this.sortField.set(field);
    }

    if (isLibrarySortDirection(direction)) {
      this.sortDirection.set(direction);
    }
  }

  private persistSort(): void {
    globalThis.localStorage.setItem(LibraryPage.SORT_KEY, `${this.sortField()}:${this.sortDirection()}`);
  }

  private buildLibraryRequestFilters(): LibraryRequestFilter {
    return {
      excludedFilters: this.filterOptions()
        .filter((filter) => filter.exclude && !this.isEmptyInstanceExclusion(filter))
        .map((filter) => filter.id),
      airedFilter: selectFilterValue('aired-filter', this.filterOptions()),
      monitoredFilter: this.optionalSelectValue('monitored-filter'),
      upgradeTargetFilter: selectFilterValue('upgrade-target-filter', this.filterOptions()),
      fileStatusFilter: selectFilterValue('file-status-filter', this.filterOptions()),
      episodeMonitoredFilter: this.optionalSelectValue('episode-monitored-filter'),
      types: this.libraryMultiSelectFilterValues('episode-type-filter'),
      statuses: this.libraryMultiSelectFilterValues('status-filter'),
      genres: this.libraryMultiSelectFilterValues('genre-filter'),
      tags: this.libraryMultiSelectFilterValues('tag-filter'),
      profiles: this.libraryMultiSelectFilterValues('profile-filter'),
      audioLanguages: this.libraryMultiSelectFilterValues('audio-language-filter'),
      subtitleLanguages: this.libraryMultiSelectFilterValues('subtitle-language-filter'),
      resolutions: this.libraryMultiSelectFilterValues('resolution-filter'),
      videoCodecs: this.libraryMultiSelectFilterValues('video-codec-filter'),
      audioCodecs: this.libraryMultiSelectFilterValues('audio-codec-filter'),
      dynamicRanges: this.libraryMultiSelectFilterValues('dynamic-range-filter'),
      containerFormats: this.libraryMultiSelectFilterValues('container-format-filter'),
      videoBitrateMin: this.videoBitrateBound('minValue'),
      videoBitrateMax: this.videoBitrateBound('maxValue'),
      scoreMin: numberRangeFilter('score-range', this.filterOptions()).minValue,
      scoreMax: numberRangeFilter('score-range', this.filterOptions()).maxValue,
      addedFrom: dateRangeFilter('added-range', this.filterOptions()).startDate,
      addedTo: dateRangeFilter('added-range', this.filterOptions()).endDate,
      airedFrom: dateRangeFilter('aired-range', this.filterOptions()).startDate,
      airedTo: dateRangeFilter('aired-range', this.filterOptions()).endDate,
      radarrInstanceFilter: this.instanceFilterValues('radarr-instance-filter'),
      sonarrInstanceFilter: this.instanceFilterValues('sonarr-instance-filter'),
      sortField: this.sortField(),
      sortDirection: this.sortDirection(),
    };
  }

  private seriesFileStatusLabel(series: SonarrLibrarySeriesTable): string {
    return this.fileStatusLabel(this.seriesIsAvailable(series));
  }

  private seriesFileStatusTone(series: SonarrLibrarySeriesTable): MediaGridDetailTone {
    return this.seriesIsAvailable(series) ? 'success' : 'danger';
  }

  private seriesIsAvailable(series: SonarrLibrarySeriesTable): boolean {
    const episodes = series.seasons.flatMap((season) => season.episodes);
    return (
      series.isAvailable ??
      ((series.fileCount ?? episodes.filter((episode) => episode.hasFile).length) > 0 &&
        !episodes.some(
          (episode) =>
            episode.hasFile === false &&
            episode.monitored !== false &&
            !!episode.airDateUtc &&
            new Date(episode.airDateUtc).getTime() <= Date.now(),
        ))
    );
  }

  protected seasonNumberLabel(seasonNumber: number): string {
    return seasonNumber === 0 ? this.i18n.t('filter.episodeSpecial') : String(seasonNumber);
  }

  protected fileStatusLabel(hasFile: boolean | undefined): string {
    return hasFile === undefined ? this.naLabel() : this.i18n.t(hasFile ? 'filter.filePresent' : 'filter.fileMissing');
  }

  protected monitoredLabel(monitored: boolean | undefined): string {
    return monitored === undefined ? this.naLabel() : this.i18n.t(monitored ? 'library.details.yes' : 'library.details.no');
  }

  private optionalSelectValue(id: string): string | undefined {
    const filter = this.filterOptions().find((option) => option.id === id);
    return filter?.type === FilterOptionType.SELECT ? (filter.selectedValue ?? undefined) : undefined;
  }

  private videoBitrateBound(bound: 'minValue' | 'maxValue'): number | null {
    const value = numberRangeFilter('video-bitrate-range', this.filterOptions())[bound];
    return value === null ? null : value * 1_000_000;
  }

  private libraryMultiSelectFilterValues(id: string): string[] {
    const values = multiSelectFilterValues(id, this.filterOptions());
    return values.length === 0 ? [DEFAULT_FILTER_ALL] : values;
  }

  private instanceFilterValues(id: string): string[] {
    const filters: readonly FilterOption[] = this.filterOptions();
    return filters.some((filter) => filter.id === id) ? this.libraryMultiSelectFilterValues(id) : [];
  }

  private effectiveInstanceFilterValues(id: string): string[] {
    const filter = this.filterOptions().find((option) => option.id === id);
    if (filter?.type !== FilterOptionType.MULTISELECT) {
      return [];
    }
    if (filter.selectedValues.length === 0) {
      return [DEFAULT_FILTER_ALL];
    }
    if (filter.selectedValues.includes(DEFAULT_FILTER_ALL)) {
      return filter.exclude ? [] : [DEFAULT_FILTER_ALL];
    }
    if (!filter.exclude) {
      return [...filter.selectedValues];
    }
    return filter.options.filter((option) => !filter.selectedValues.includes(option.value)).map((option) => option.value);
  }

  private isEmptyInstanceExclusion(filter: FilterOption): boolean {
    const isInstanceFilter = filter.id === 'radarr-instance-filter' || filter.id === 'sonarr-instance-filter';
    return isInstanceFilter && filter.type === FilterOptionType.MULTISELECT && filter.selectedValues.length === 0;
  }

  private mergeFilterValues(a: string[] | undefined, b: string[] | undefined): string[] {
    const merged = new Map<string, string>();
    for (const value of [...(a ?? []), ...(b ?? [])]) {
      const key = value.toLowerCase();
      if (!merged.has(key)) merged.set(key, value);
    }
    return [...merged.values()].sort((a, b) => a.localeCompare(b));
  }

  private filterValueCount(filterId: string, value: string | null): number | undefined {
    if (value === null) return undefined;
    const filtered = this.filteredCounts();
    if (filtered !== null) return filtered[filterId]?.[value.toLowerCase()] ?? 0;
    const counts = this.arrInstanceFilters().counts?.[filterId];
    if (!counts) return undefined;
    return counts[value.toLowerCase()] ?? 0;
  }

  private mergeFilterCounts(a: LibraryFilterCounts | undefined, b: LibraryFilterCounts | undefined): LibraryFilterCounts {
    const merged = new Map<string, Map<string, number>>();
    for (const source of [a, b]) {
      for (const [filterId, counts] of Object.entries(source ?? {})) {
        const values = merged.get(filterId) ?? new Map<string, number>();
        for (const [value, count] of Object.entries(counts)) {
          const key = value.toLowerCase();
          values.set(key, (values.get(key) ?? 0) + count);
        }
        merged.set(filterId, values);
      }
    }
    return Object.fromEntries([...merged].map(([filterId, counts]) => [filterId, Object.fromEntries(counts)]));
  }

  private handelLoadError(error: unknown) {
    this.loadError.set(error instanceof Error && error.message ? error.message : this.i18n.t('common.error.loadData'));
    this.loading.set(false);
  }
}

function latestSeriesAirDate(item: MediaGridItem): string | null {
  const episodes = (item.seriesData?.seasons ?? []).flatMap((season) => season.episodes);

  return latestAirDate(episodes.map((episode) => episode.airDateUtc));
}

function isLibrarySortField(value: string | undefined): value is LibrarySortField {
  return value !== undefined && (Object.values(LibrarySortField) as string[]).includes(value);
}

function isLibrarySortDirection(value: string | undefined): value is LibrarySortDirection {
  return value !== undefined && (Object.values(LibrarySortDirection) as string[]).includes(value);
}
