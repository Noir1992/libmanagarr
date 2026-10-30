import { DatePipe, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, LOCALE_ID, signal, Signal, WritableSignal } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef, MatDialog } from '@angular/material/dialog';
import { MediaDetailData } from '../../interfaces/general/media-detail-data';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';
import { MediaGridDetailTone, MediaGridItem } from '../../interfaces/general/media-grid-item';
import { AppI18nService } from '../../services/app-i18n.service';
import { TranslationKey } from '../../services/i18n/_i18n-key';
import { airStatusTone, fileStatusTone } from '../../utils/media-status-tone.util';
import { specialsLast } from '../../utils/media-detail-utils';

@Component({
  selector: 'app-sonarr-series-dialog',
  imports: [DatePipe, MatDialogModule, MatIconButton],
  templateUrl: './sonarr-series-dialog.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './sonarr-series-dialog.widget.scss',
})
export class SonarrSeriesDialogWidget {
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialogRef: MatDialogRef<SonarrSeriesDialogWidget> = inject(MatDialogRef<SonarrSeriesDialogWidget>);
  private readonly dialog = inject(MatDialog);
  private readonly locale = inject(LOCALE_ID);
  readonly data: MediaGridItem = inject(MAT_DIALOG_DATA);

  protected readonly seasons: Signal<
    {
      number: number;
      episodes: {
        details?: MediaDetailData;
        number: number;
        title?: string;
        hasFile?: boolean;
        monitored?: boolean;
        customFormatScore?: number | null;
        airDateUtc?: string | null;
      }[];
    }[]
  > = computed(() => {
    return specialsLast(this.data.seriesData?.seasons ?? []);
  });

  protected readonly seriesRows = computed(() => {
    const series = this.data.seriesData;
    const na = this.i18n.t('common.na');
    const row = (key: TranslationKey, value: string | undefined, tone?: MediaGridDetailTone) => ({
      key,
      label: this.i18n.t(key),
      value: value || na,
      tone,
    });
    const flag = (value: boolean | undefined, yes: TranslationKey, no: TranslationKey) =>
      value === undefined ? na : this.i18n.t(value ? yes : no);
    return [
      row('common.column.instance', series?.instanceName),
      row('common.column.profile', series?.qualityProfileName),
      row('filter.airStatus', series?.status, airStatusTone(series?.status)),
      row('filter.addedRange', series?.addedAt ? formatDate(series.addedAt, 'medium', this.locale) : na),
      row('filter.fileStatus', flag(series?.isAvailable, 'filter.filePresent', 'filter.fileMissing'), fileStatusTone(series?.isAvailable)),
      row(
        'library.details.totalSize',
        this.data.size == null ? na : `${(this.data.size / 1024 ** 3).toLocaleString(this.locale, { maximumFractionDigits: 2 })} GiB`,
      ),
      row('filter.seriesMonitored', flag(series?.monitored, 'library.details.yes', 'library.details.no')),
      row('filter.genres', series?.genres?.join(', ')),
      row('filter.tags', series?.tags?.join(', ')),
    ];
  });
  protected readonly seasonLabel: Signal<string> = computed(() => this.i18n.t('common.column.season'));
  protected readonly episodeLabel: Signal<string> = computed(() => this.i18n.t('common.column.episode'));
  protected readonly titleLabel: Signal<string> = computed(() => this.i18n.t('common.column.title'));
  protected readonly airDateLabel: Signal<string> = computed(() => this.i18n.t('common.column.airDate'));
  protected readonly naLabel: Signal<string> = computed(() => this.i18n.t('common.na'));
  protected readonly closeButtonLabel: Signal<string> = computed(() => this.i18n.t('common.close'));

  /* Expanded state per season (all expanded by default) */
  protected readonly expandedSeasons: WritableSignal<Set<number>> = signal(new Set(this.seasons().map((s) => s.number)));

  protected openEpisode(details: MediaDetailData | undefined): void {
    if (!details) return;
    this.dialog.open(MediaDetailsDialogWidget, { data: details, width: '700px', maxWidth: '95vw' });
  }

  protected seasonTitle(seasonNumber: number): string {
    return seasonNumber === 0 ? this.i18n.t('filter.episodeSpecial') : `${this.seasonLabel()} ${seasonNumber}`;
  }

  protected instanceLinkLabel(name: string): string {
    return this.i18n.t('library.details.open', { name });
  }

  protected close(): void {
    this.dialogRef.close();
  }

  protected toggleSeason(seasonNumber: number): void {
    this.expandedSeasons.update((set) => {
      const next = new Set(set);
      if (next.has(seasonNumber)) {
        next.delete(seasonNumber);
      } else {
        next.add(seasonNumber);
      }
      return next;
    });
  }

  protected isSeasonExpanded(seasonNumber: number): boolean {
    return this.expandedSeasons().has(seasonNumber);
  }
}
