import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MediaDetailData } from '../../interfaces/general/media-detail-data';
import { MediaGridDetailTone } from '../../interfaces/general/media-grid-item';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { TranslationKey } from '../../services/i18n/_i18n-key';
import { airStatusLabel } from '../../utils/air-status-label.util';
import { airStatusTone, fileStatusTone } from '../../utils/media-status-tone.util';

@Component({
  selector: 'app-media-details-dialog',
  imports: [MatDialogModule, MatIconButton],
  templateUrl: './media-details-dialog.widget.html',
  styleUrl: './media-details-dialog.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MediaDetailsDialogWidget {
  protected readonly i18n = inject(AppI18nService);
  private readonly settings = inject(AppSettingsService);
  private readonly dialogRef = inject(MatDialogRef<MediaDetailsDialogWidget>);
  readonly data: MediaDetailData = inject(MAT_DIALOG_DATA);
  protected readonly closeLabel = computed(() => this.i18n.t('common.close'));
  protected readonly scoreTone = computed(() => {
    const score = this.data.hasFile === false ? null : this.data.customFormatScore;
    const target = this.data.upgradeUntilCustomFormatScore;
    if (score == null || target == null) return null;
    return score >= target ? 'reached' : 'below';
  });
  protected readonly rows = computed(() => {
    const d = this.data;
    const file = d.hasFile === false ? null : d.mediaDetails;
    const size = d.hasFile === false ? null : (file?.size ?? d.size);
    const na = this.i18n.t('common.na');
    const value = (v: string | number | null | undefined) => (v == null || v === '' ? na : String(v));
    const list = (v: string[] | undefined) => (v?.length ? v.join(', ') : na);
    const date = (v: string | null | undefined) => (v ? formatDate(v, 'medium', this.settings.localeId()) : na);
    const flag = (v: boolean | undefined, yes: TranslationKey, no: TranslationKey) => (v === undefined ? na : this.i18n.t(v ? yes : no));
    const row = (key: TranslationKey, v: string, tone?: MediaGridDetailTone) => ({ key, label: this.i18n.t(key), value: v, tone });
    const score = d.hasFile === false ? null : d.customFormatScore;
    const target = d.upgradeUntilCustomFormatScore;
    const upgrade =
      score == null || target == null ? na : this.i18n.t(score < target ? 'filter.upgradeTargetBelow' : 'filter.upgradeTargetReached');
    let upgradeTone: MediaGridDetailTone | undefined;
    if (score != null && target != null) {
      upgradeTone = score < target ? 'danger' : 'success';
    }
    const duration = row(
      'library.details.duration',
      file?.durationSeconds == null
        ? na
        : `${Math.floor(file.durationSeconds / 3600)}:${String(Math.floor(file.durationSeconds / 60) % 60).padStart(2, '0')}:${String(Math.floor(file.durationSeconds) % 60).padStart(2, '0')}`,
    );
    const libraryStatus = row('filter.fileStatus', flag(d.hasFile, 'filter.filePresent', 'filter.fileMissing'), fileStatusTone(d.hasFile));
    const rows = [];
    if (d.episodeNumber != null) {
      let airedTone: MediaGridDetailTone | undefined;
      if (d.hasAired !== undefined) {
        airedTone = d.hasAired ? 'success' : 'warning';
      }
      rows.push(
        row('common.column.episode', value(d.episodeNumber)),
        row('common.column.season', value(d.seasonNumber)),
        row('filter.episodeType', this.i18n.t(d.seasonNumber === 0 ? 'filter.episodeSpecial' : 'filter.episodeRegular')),
        duration,
        row('common.column.airDate', date(d.airDateUtc)),
        row('filter.airStatus', flag(d.hasAired, 'filter.airedYes', 'filter.airedNo'), airedTone),
        libraryStatus,
        row('filter.episodeMonitored', flag(d.monitored, 'library.details.yes', 'library.details.no')),
      );
    } else {
      rows.push(
        row('common.column.airDate', date(d.airDateUtc)),
        row('filter.airStatus', airStatusLabel(d.status, this.i18n), airStatusTone(d.status)),
        libraryStatus,
        duration,
        row('filter.genres', list(d.genres)),
        row('common.column.instance', value(d.instanceName)),
        row('filter.monitored', flag(d.monitored, 'library.details.yes', 'library.details.no')),
        row('filter.addedRange', date(d.addedAt)),
        row('common.column.profile', value(d.qualityProfileName)),
      );
    }
    rows.push(
      row('common.column.targetScore', value(target)),
      row('common.column.score', value(score)),
      row('filter.upgradeTarget', upgrade, upgradeTone),
      row('filter.resolution', value(file?.resolution)),
      row('library.details.originalResolution', value(file?.originalResolution)),
      row('filter.videoCodec', value(file?.videoCodec)),
      row('filter.audioCodec', value(file?.audioCodec)),
      row('filter.dynamicRange', value(file?.dynamicRange)),
      row('filter.containerFormat', value(file?.containerFormat)),
      row(
        'filter.videoBitrate',
        file?.videoBitrate ? (file.videoBitrate / 1000000).toLocaleString(this.settings.localeId(), { maximumFractionDigits: 3 }) : na,
      ),
      row('filter.audioLanguages', list(file?.audioLanguages)),
      row('filter.subtitleLanguages', list(file?.subtitleLanguages)),
      row('library.details.releaseGroup', value(file?.releaseGroup)),
      row('library.details.filePath', value(file?.relativePath)),
      row('library.details.createdAt', date(file?.createdAt)),
      row(
        'library.details.size',
        size == null ? na : `${(size / 1024 ** 3).toLocaleString(this.settings.localeId(), { maximumFractionDigits: 2 })} GiB`,
      ),
    );
    if (d.episodeNumber == null) {
      rows.push(row('filter.tags', list(d.tags)));
    }
    return rows;
  });

  protected instanceLinkLabel(name: string): string {
    return this.i18n.t('library.details.open', { name });
  }

  protected close(): void {
    this.dialogRef.close();
  }
}
