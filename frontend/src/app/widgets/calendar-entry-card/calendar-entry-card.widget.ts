import { ChangeDetectionStrategy, Component, computed, inject, input, InputSignal, Signal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { CalendarEntry } from '../../interfaces/calendar/calendar-entry';
import { AppI18nService } from '../../services/app-i18n.service';
import {
  CALENDAR_RELEASE_TYPE_LABEL_KEYS,
  CALENDAR_STATUS_ICON_NAMES,
  CALENDAR_STATUS_LABEL_KEYS,
  CalendarEntryStatus,
  deriveEntryStatus,
  entryTimeLabel,
  episodeLabel,
  isEntryPast,
} from '../../pages/calendar/calendar.utils';

@Component({
  selector: 'app-calendar-entry-card',
  imports: [MatIcon],
  templateUrl: './calendar-entry-card.widget.html',
  styleUrl: './calendar-entry-card.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarEntryCardWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);

  readonly entry: InputSignal<CalendarEntry> = input.required<CalendarEntry>();
  readonly now: InputSignal<Date> = input.required<Date>();
  readonly showInstance: InputSignal<boolean> = input<boolean>(true);

  protected readonly status: Signal<CalendarEntryStatus> = computed((): CalendarEntryStatus => deriveEntryStatus(this.entry(), this.now()));
  protected readonly isPast: Signal<boolean> = computed((): boolean => isEntryPast(this.entry(), this.now()));
  protected readonly statusLabel: Signal<string> = computed((): string => this.i18n.t(CALENDAR_STATUS_LABEL_KEYS[this.status()]));
  protected readonly statusIcon: Signal<string> = computed((): string => CALENDAR_STATUS_ICON_NAMES[this.status()]);
  protected readonly episodeCode: Signal<string | null> = computed((): string | null => episodeLabel(this.entry()));
  protected readonly timeText: Signal<string> = computed((): string => entryTimeLabel(this.entry()) ?? this.i18n.t('calendar.allDay'));
  protected readonly releaseTypeLabel: Signal<string | null> = computed((): string | null => {
    const releaseType = this.entry().releaseType;
    return releaseType === 'Episode' ? null : this.i18n.t(CALENDAR_RELEASE_TYPE_LABEL_KEYS[releaseType]);
  });
  protected readonly typeAttr: Signal<'movie' | 'tv'> = computed((): 'movie' | 'tv' =>
    this.entry().application === 'Radarr' ? 'movie' : 'tv',
  );
  protected readonly typeIcon: Signal<string> = computed((): string => (this.typeAttr() === 'movie' ? 'movie' : 'live_tv'));
}
