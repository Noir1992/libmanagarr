import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, Signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { CalendarEntry } from '../../interfaces/calendar/calendar-entry';
import { ArrInstanceSettings } from '../../interfaces/settings/arr-instance-settings';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import {
  CALENDAR_RELEASE_TYPE_LABEL_KEYS,
  CALENDAR_STATUS_ICON_NAMES,
  CALENDAR_STATUS_LABEL_KEYS,
  CalendarEntryStatus,
  deriveEntryStatus,
  entryDayKey,
  entryReleaseTypeIcon,
  entryTimeLabel,
  episodeLabel,
  parseDayKey,
} from '../../pages/calendar/calendar.utils';
import { normalizeBaseUrl } from '../../utils/base-utils';

export interface CalendarEntryDialogData {
  readonly entry: CalendarEntry;
  readonly now: Date;
}

@Component({
  selector: 'app-calendar-entry-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIcon],
  templateUrl: './calendar-entry-dialog.widget.html',
  styleUrl: './calendar-entry-dialog.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarEntryDialogWidget {
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly dialogRef: MatDialogRef<CalendarEntryDialogWidget> = inject(MatDialogRef<CalendarEntryDialogWidget>);
  readonly data: CalendarEntryDialogData = inject(MAT_DIALOG_DATA);

  protected readonly closeLabel: Signal<string> = computed((): string => this.i18n.t('common.close'));
  protected readonly status: Signal<CalendarEntryStatus> = computed((): CalendarEntryStatus =>
    deriveEntryStatus(this.data.entry, this.data.now),
  );
  protected readonly statusLabel: Signal<string> = computed((): string => this.i18n.t(CALENDAR_STATUS_LABEL_KEYS[this.status()]));
  protected readonly statusIcon: Signal<string> = computed((): string => CALENDAR_STATUS_ICON_NAMES[this.status()]);
  protected readonly episodeCode: Signal<string | null> = computed((): string | null => episodeLabel(this.data.entry));
  protected readonly releaseTypeLabel: Signal<string | null> = computed((): string | null => {
    const releaseType = this.data.entry.releaseType;
    return releaseType === 'Episode' ? null : this.i18n.t(CALENDAR_RELEASE_TYPE_LABEL_KEYS[releaseType]);
  });
  protected readonly releaseTypeIcon: Signal<string | null> = computed((): string | null => entryReleaseTypeIcon(this.data.entry));
  // Deep link into the Arr instance, built like the library rows do: instance baseUrl plus the
  // series or movie slug. Without either piece there is no target and the row stays plain text.
  protected readonly instanceUrl: Signal<string | null> = computed((): string | null => {
    const entry = this.data.entry;
    if (entry.titleSlug === null) {
      return null;
    }

    const instances: ArrInstanceSettings[] =
      entry.application === 'Radarr' ? this.appSettingsService.radarrInstances() : this.appSettingsService.sonarrInstances();
    const instance: ArrInstanceSettings | undefined = instances.find(
      (candidate: ArrInstanceSettings): boolean => candidate.id === entry.instanceId,
    );
    if (instance === undefined) {
      return null;
    }

    const section: string = entry.application === 'Radarr' ? 'movie' : 'series';
    return `${normalizeBaseUrl(instance.baseUrl)}/${section}/${entry.titleSlug}`;
  });
  protected readonly dateLabel: Signal<string> = computed((): string =>
    formatDate(parseDayKey(entryDayKey(this.data.entry)), 'EEEE, d. MMMM y', this.appSettingsService.localeId()),
  );
  protected readonly timeLabel: Signal<string> = computed((): string => entryTimeLabel(this.data.entry) ?? this.i18n.t('calendar.allDay'));

  protected readonly statusRowLabel: Signal<string> = computed((): string => this.i18n.t('common.column.status'));
  protected readonly seasonEpisodeRowLabel: Signal<string> = computed((): string => this.i18n.t('calendar.column.seasonEpisode'));
  protected readonly episodeTitleRowLabel: Signal<string> = computed((): string => this.i18n.t('calendar.column.episodeTitle'));
  protected readonly releaseTypeRowLabel: Signal<string> = computed((): string => this.i18n.t('calendar.filter.label'));
  protected readonly dateRowLabel: Signal<string> = computed((): string => this.i18n.t('calendar.column.date'));
  protected readonly timeRowLabel: Signal<string> = computed((): string => this.i18n.t('calendar.column.time'));
  protected readonly instanceRowLabel: Signal<string> = computed((): string => this.i18n.t('calendar.detail.instance'));
  protected readonly instanceLinkLabel: Signal<string> = computed((): string =>
    this.i18n.t('library.details.open', { name: this.data.entry.instanceName }),
  );

  protected close(): void {
    this.dialogRef.close();
  }
}
