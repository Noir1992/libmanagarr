import { Subscription } from 'rxjs';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  OnDestroy,
  OnInit,
  signal,
  Signal,
  WritableSignal,
} from '@angular/core';
import { CodecMediaFile } from '../../interfaces/codec-statistic/codec-media-file';
import { CodecStatisticItem } from '../../interfaces/codec-statistic/codec-statistic-item';
import { CodecStatisticsResponse } from '../../interfaces/codec-statistic/codec-statistics-response';
import { DEFAULT_FILTER_ALL, FilterMultiSelectOption, FilterOption } from '../../interfaces/filter-option';
import { FilterOptionType } from '../../enums/filter-option-type';
import { LibraryRequestFilter } from '../../interfaces/library/library-request-filter';
import { MediaTreeNode } from '../../interfaces/media-tree/media-tree-node';
import { ArrInstanceSettings } from '../../interfaces/settings/arr-instance-settings';
import { FormatBytesPipe } from '../../pipes/format-bytes.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { CodecStatisticsApiService } from '../../services/codec-statistics-api.service';
import {
  CodecStatisticsChartCategory as ChartCategory,
  CodecStatisticsChartSlice as ChartSlice,
} from '../../types/codec-statistics/codec-statistics-chart';
import { normalizeBaseUrl } from '../../utils/base-utils';
import { STATISTICS_CHART_CIRCUMFERENCE } from '../../utils/statistics-chart-geometry';
import { groupResolutionStatistics, normalizeResolutionGroup, RESOLUTION_GROUPS } from '../../utils/resolution-statistics-utils';
import { multiSelectFilterValues, numberRangeFilter } from '../../utils/filter-utils';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { NoArrInstancesConfiguredSectionWidget } from '../../widgets/sections/no-arr-instances-configured-section/no-arr-instances-configured-section.widget';
import { NoDataSectionWidget } from '../../widgets/sections/no-data-section/no-data-section.widget';
import { StatisticsChartWidget } from '../../widgets/statistics-chart/statistics-chart.widget';

@Component({
  selector: 'app-codec-statistics-page',
  imports: [
    PageFilterWidget,
    StatisticsChartWidget,
    LoadingSectionWidget,
    LoadingErrorSectionWidget,
    NoArrInstancesConfiguredSectionWidget,
    NoDataSectionWidget,
  ],
  templateUrl: './codec-statistics.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './codec-statistics.page.scss',
})
export class CodecStatisticsPage implements OnInit, OnDestroy {
  /* Injections */
  private readonly formatBytesPipe: FormatBytesPipe = new FormatBytesPipe();
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly codecStatisticsApi: CodecStatisticsApiService = inject(CodecStatisticsApiService);
  private readonly i18n: AppI18nService = inject(AppI18nService);

  /* I18n signals */
  protected readonly title: Signal<string> = computed((): string => this.i18n.t('codecStats.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('codecStats.description'));

  /* Signals */
  protected readonly loading: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly loadError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly stats: WritableSignal<CodecStatisticsResponse | null> = signal<CodecStatisticsResponse | null>(null);
  protected readonly sonarrInstances: Signal<readonly ArrInstanceSettings[]> = computed(() => this.appSettingsService.sonarrInstances());
  protected readonly radarrInstances: Signal<readonly ArrInstanceSettings[]> = computed(() => this.appSettingsService.radarrInstances());
  protected readonly loadSettingsError: Signal<string | null> = this.appSettingsService.loadError;
  protected readonly isFiltered: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly noInstancesConfigured: Signal<boolean> = computed(
    (): boolean => this.sonarrInstances().length === 0 && this.radarrInstances().length === 0,
  );

  protected readonly chartCategories: Signal<ChartCategory[]> = computed((): ChartCategory[] => {
    const current: CodecStatisticsResponse | null = this.stats();
    if (current === null) {
      return [];
    }

    return [
      this.toCategory('containerFormats', this.i18n.t('codecStats.containerFormats'), current.containerFormats),
      this.toCategory('resolutions', this.i18n.t('codecStats.resolutions'), groupResolutionStatistics(current.resolutions)),
      this.toCategory('dynamicRanges', this.i18n.t('codecStats.dynamicRanges'), current.dynamicRanges),
      this.toCategory('videoCodecs', this.i18n.t('codecStats.videoCodecs'), current.videoCodecs),
      this.toCategory('audioCodecs', this.i18n.t('codecStats.audioCodecs'), current.audioCodecs),
      this.toCategory('videoBitrates', this.i18n.t('filter.videoBitrate'), current.videoBitrates ?? []),
      this.toCategory('audioLanguages', this.i18n.t('codecStats.audioLanguages'), current.audioLanguages ?? []),
      this.toCategory('subtitleLanguages', this.i18n.t('codecStats.subtitleLanguages'), current.subtitleLanguages ?? []),
    ];
  });
  protected readonly noDataAvailable: Signal<boolean> = computed(
    (): boolean => this.stats() !== null && this.chartCategories().every((category) => category.items.length === 0),
  );
  private readonly filterDefinitions: Signal<readonly FilterOption[]> = computed((): readonly FilterOption[] => {
    const filters: FilterOption[] = [];

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
        onChanged: () => this.scheduleLoadStats(),
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
        onChanged: () => this.scheduleLoadStats(),
      });
    }

    const current = this.stats();
    const values = current?.filterValues;
    const mediaFilters = [
      [
        'resolution-filter',
        'filter.resolution',
        values?.resolutions ?? current?.resolutions.map((item) => normalizeResolutionGroup(item.label)) ?? [],
      ],
      [
        'container-format-filter',
        'filter.containerFormat',
        values?.containerFormats ?? current?.containerFormats.map((item) => item.label) ?? [],
      ],
      ['video-codec-filter', 'filter.videoCodec', values?.videoCodecs ?? current?.videoCodecs.map((item) => item.label) ?? []],
      ['audio-codec-filter', 'filter.audioCodec', values?.audioCodecs ?? current?.audioCodecs.map((item) => item.label) ?? []],
      ['dynamic-range-filter', 'filter.dynamicRange', values?.dynamicRanges ?? current?.dynamicRanges.map((item) => item.label) ?? []],
      ['audio-language-filter', 'filter.audioLanguages', values?.audioLanguages ?? current?.audioLanguages.map((item) => item.label) ?? []],
      [
        'subtitle-language-filter',
        'filter.subtitleLanguages',
        values?.subtitleLanguages ?? current?.subtitleLanguages.map((item) => item.label) ?? [],
      ],
    ] as const;
    for (const [id, label, options] of mediaFilters) {
      const sorted = [...new Set(options)].sort((a, b) =>
        id === 'resolution-filter'
          ? RESOLUTION_GROUPS.indexOf(normalizeResolutionGroup(a)) - RESOLUTION_GROUPS.indexOf(normalizeResolutionGroup(b))
          : a.localeCompare(b),
      );
      filters.push({
        id,
        label: this.i18n.t(label),
        type: FilterOptionType.MULTISELECT,
        options: sorted.map((value) => ({ value, label: value })),
        selectedValues: [],
        ...(id === 'resolution-filter'
          ? { normalizeValue: (value: string) => (value === DEFAULT_FILTER_ALL ? value : normalizeResolutionGroup(value)) }
          : {}),
        onChanged: () => this.scheduleLoadStats(),
      });
    }
    filters.push({
      id: 'video-bitrate-range',
      label: this.i18n.t('filter.videoBitrate'),
      type: FilterOptionType.NUMBER_RANGE,
      minimum: 0,
      step: 'any',
      minValue: null,
      maxValue: null,
      onChanged: () => this.scheduleLoadStats(true),
    });
    return filters.map((filter) => {
      if (filter.type !== FilterOptionType.MULTISELECT) return { ...filter, supportsExclusion: true };
      const counts = current?.counts?.[filter.id] ?? {};
      return {
        ...filter,
        supportsExclusion: true,
        totalCount: counts[DEFAULT_FILTER_ALL] ?? 0,
        options: filter.options.map((option) => ({
          ...option,
          count: Object.entries(counts).find(([key]) => key.toLowerCase() === option.value.toLowerCase())?.[1] ?? 0,
        })),
      };
    });
  });

  protected readonly filterOptions: Signal<readonly FilterOption[]> = linkedSignal<readonly FilterOption[], readonly FilterOption[]>({
    source: this.filterDefinitions,
    computation: (filters, previous) =>
      filters.map((filter) => {
        const selected = previous?.value.find((option) => option.id === filter.id);
        const preserved = { ...filter, exclude: selected?.exclude ?? false };
        if (preserved.type === FilterOptionType.MULTISELECT && selected?.type === FilterOptionType.MULTISELECT)
          return { ...preserved, selectedValues: [...selected.selectedValues] };
        if (preserved.type === FilterOptionType.NUMBER_RANGE && selected?.type === FilterOptionType.NUMBER_RANGE)
          return { ...preserved, minValue: selected.minValue, maxValue: selected.maxValue };
        return preserved;
      }),
  });
  /* Runtime state */
  private loadStatsQueued: boolean = false;
  private loadRequestId: number = 0;

  private activeRequest?: Subscription;
  private debounceTimer?: ReturnType<typeof setTimeout>;
  private destroyed = false;

  ngOnDestroy(): void {
    this.destroyed = true;
    this.loadRequestId++;
    this.activeRequest?.unsubscribe();
    clearTimeout(this.debounceTimer);
  }

  /* Lifecycle */
  ngOnInit(): void {
    void this.initializeAsync();
  }

  /* Helpers */
  private async initializeAsync(): Promise<void> {
    this.loading.set(true);
    try {
      await this.appSettingsService.loadInstances();
    } catch (error) {
      this.loadError.set(
        error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('common.error.loadData'),
      );
      this.loading.set(false);
      return;
    }

    if (this.noInstancesConfigured()) {
      this.loading.set(false);
      return;
    }

    this.scheduleLoadStats();
  }

  private scheduleLoadStats(debounce = false): void {
    if (this.destroyed) return;
    this.loadRequestId++;
    this.activeRequest?.unsubscribe();
    clearTimeout(this.debounceTimer);
    if (debounce) {
      this.loadStatsQueued = false;
      this.debounceTimer = setTimeout(() => this.loadStats(), 200);
      return;
    }
    if (this.loadStatsQueued) {
      return;
    }

    this.loadStatsQueued = true;
    queueMicrotask(() => {
      if (!this.loadStatsQueued) return;
      this.loadStatsQueued = false;
      this.loadStats();
    });
  }

  private loadStats(): void {
    if (this.destroyed) return;
    clearTimeout(this.debounceTimer);
    this.activeRequest?.unsubscribe();
    const loadRequestId: number = ++this.loadRequestId;
    this.loading.set(true);
    this.loadError.set(null);

    const selectedSonarrIds = this.instanceValues('sonarr-instance-filter');
    const selectedRadarrIds = this.instanceValues('radarr-instance-filter');

    this.activeRequest = this.codecStatisticsApi.fetchCodecStatistics(selectedSonarrIds, selectedRadarrIds, this.mediaFilters()).subscribe({
      next: (response) => {
        if (loadRequestId !== this.loadRequestId) {
          return;
        }

        this.stats.set(response);
        this.loading.set(false);
      },
      error: () => {
        if (loadRequestId !== this.loadRequestId) {
          return;
        }

        this.loadError.set(this.i18n.t('common.tableLoadError'));
        this.loading.set(false);
      },
    });
  }

  private instanceValues(id: string): string[] {
    const filters = this.filterOptions();
    if (!filters.some((filter) => filter.id === id)) return [];
    const values = multiSelectFilterValues(id, filters);
    return values.length === 0 ? [DEFAULT_FILTER_ALL] : values;
  }

  private mediaFilters(): Partial<LibraryRequestFilter> {
    const filters = this.filterOptions();
    const bitrate = numberRangeFilter('video-bitrate-range', filters);
    const selectedValues = (id: string) => {
      const values = multiSelectFilterValues(id, filters);
      return values.length === 0 ? [DEFAULT_FILTER_ALL] : values;
    };
    return {
      excludedFilters: filters.filter((filter) => filter.exclude).map((filter) => filter.id),
      resolutions: selectedValues('resolution-filter'),
      containerFormats: selectedValues('container-format-filter'),
      videoCodecs: selectedValues('video-codec-filter'),
      audioCodecs: selectedValues('audio-codec-filter'),
      dynamicRanges: selectedValues('dynamic-range-filter'),
      audioLanguages: selectedValues('audio-language-filter'),
      subtitleLanguages: selectedValues('subtitle-language-filter'),
      videoBitrateMin: bitrate.minValue === null ? null : bitrate.minValue * 1_000_000,
      videoBitrateMax: bitrate.maxValue === null ? null : bitrate.maxValue * 1_000_000,
    };
  }

  private toCategory(key: string, title: string, items: CodecStatisticItem[]): ChartCategory {
    return {
      key,
      title,
      items: this.toSlices(items),
    };
  }

  private toSlices(items: CodecStatisticItem[]): ChartSlice[] {
    const palette: string[] = ['#4ea8de', '#80ed99', '#ffd166', '#ef476f', '#b388eb', '#06d6a0', '#f4a261', '#ff8fab'];
    const circumference = STATISTICS_CHART_CIRCUMFERENCE;
    let offset: number = 0;

    return [...items]
      .sort((a, b) => b.percentage - a.percentage || b.count - a.count || a.label.localeCompare(b.label))
      .map((item, index) => {
        const length: number = (item.percentage / 100) * circumference;
        let treeRoots: MediaTreeNode[] | undefined;
        const buildTree = () => this.buildTree(item.mediaFiles);
        const slice: ChartSlice = {
          ...item,
          color: palette[index % palette.length],
          dashArray: `${length} ${Math.max(0, circumference - length)}`,
          dashOffset: `${-offset}`,
          tooltip: `${item.label} - ${item.percentage}% - ${item.count} ${this.i18n.t('codecStats.mediaFiles')}`,
          metaText: `${item.count} files · ${this.formatBytesPipe.transform(item.totalSizeBytes ?? 0)}`,
          get treeRoots() {
            return (treeRoots ??= buildTree());
          },
        };
        offset += length;
        return slice;
      });
  }

  private buildTree(files: CodecMediaFile[]): MediaTreeNode[] {
    const roots: Map<string, MediaTreeNode> = new Map<string, MediaTreeNode>();
    const nodes = new Map<string, MediaTreeNode>();

    for (const file of files) {
      const rootKey: string = `root::${file.source}`;
      const rootNode: MediaTreeNode = roots.get(rootKey) ?? {
        key: rootKey,
        label: file.source,
        isDirectory: true,
        children: [],
        fileCount: 0,
        totalSizeBytes: 0,
        source: file.source,
        instanceName: file.instanceName,
      };

      if (!roots.has(rootKey)) {
        roots.set(rootKey, rootNode);
      }

      const segments: string[] = this.pathSegmentsFromRoot(file);
      const url = this.toArrItemUrl(file);
      let current: MediaTreeNode = rootNode;
      current.fileCount += 1;
      current.totalSizeBytes += file.sizeBytes ?? 0;

      for (let i: number = 0; i < segments.length; i++) {
        const segment: string = segments[i];
        const isFile: boolean = i === segments.length - 1;
        const nodeKey: string = `${current.key}/${segment.toLowerCase()}`;

        let child: MediaTreeNode | undefined = nodes.get(nodeKey);
        if (!child) {
          child = {
            key: nodeKey,
            label: segment,
            isDirectory: !isFile,
            children: [],
            fileCount: 0,
            totalSizeBytes: 0,
            source: file.source,
            instanceName: file.instanceName,
            url,
            ...(isFile ? { file } : {}),
          };
          current.children.push(child);
          nodes.set(nodeKey, child);
        }

        child.fileCount += 1;
        child.totalSizeBytes += file.sizeBytes ?? 0;
        current = child;
      }
    }

    return Array.from(roots.values());
  }

  private pathSegmentsFromRoot(file: CodecMediaFile): string[] {
    if (this.isRadarrSource(file.source)) {
      return [this.toRadarrFileLabel(file)];
    }

    const rawPath: string = (file.relativePath ?? '').replaceAll('\\', '/');
    const allSegments: string[] = rawPath.split('/').filter((segment) => segment.length > 0);

    if (allSegments.length === 0) {
      return this.fallbackSegmentsWithoutPath(file);
    }

    return [file.rootTitle, ...allSegments];
  }

  private isRadarrSource(source: string): boolean {
    return source.trimStart().toLowerCase().startsWith('radarr');
  }

  private toRadarrFileLabel(file: CodecMediaFile): string {
    const rawPath: string = (file.relativePath ?? '').replaceAll('\\', '/');
    const allSegments: string[] = rawPath.split('/').filter((segment) => segment.length > 0);
    return allSegments.length > 0 ? allSegments.at(-1)! : file.title;
  }

  private fallbackSegmentsWithoutPath(file: CodecMediaFile): string[] {
    const sonarrEpisodeMatch: RegExpMatchArray | null = new RegExp(/\bS(\d{1,2})E\d{1,3}\b/i).exec(file.title);
    if (sonarrEpisodeMatch) {
      const seasonNumber: number = Number.parseInt(sonarrEpisodeMatch[1], 10);
      const seasonLabel: string = Number.isNaN(seasonNumber) ? 'Season' : `Season ${seasonNumber.toString().padStart(2, '0')}`;
      return [file.rootTitle, seasonLabel, file.title];
    }

    return [file.rootTitle, file.title];
  }

  private toArrItemUrl(file: CodecMediaFile): string | null {
    const source: string = (file.source ?? '').trim().toLowerCase();
    if (source.startsWith('sonarr')) {
      if (file.instanceName == null || !file.titleSlug) {
        return null;
      }

      const instance: ArrInstanceSettings | undefined = this.sonarrInstances().find((entry) => entry.name === file.instanceName);
      if (!instance) {
        return null;
      }

      return `${normalizeBaseUrl(instance.baseUrl)}/series/${file.titleSlug}`;
    }

    if (source.startsWith('radarr')) {
      if (file.instanceName == null || !file.titleSlug) {
        return null;
      }

      const instance: ArrInstanceSettings | undefined = this.radarrInstances().find((entry) => entry.name === file.instanceName);
      if (!instance) {
        return null;
      }

      return `${normalizeBaseUrl(instance.baseUrl)}/movie/${file.titleSlug}`;
    }

    return null;
  }
}
