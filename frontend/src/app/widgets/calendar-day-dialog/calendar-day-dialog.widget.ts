import { ChangeDetectionStrategy, Component, computed, inject, Signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { CalendarEntry } from '../../interfaces/calendar/calendar-entry';
import { AppI18nService } from '../../services/app-i18n.service';
import { CalendarEntryCardWidget } from '../calendar-entry-card/calendar-entry-card.widget';
import { CalendarEntryDialogData, CalendarEntryDialogWidget } from '../calendar-entry-dialog/calendar-entry-dialog.widget';

export interface CalendarDayDialogData {
  readonly title: string;
  readonly entries: readonly CalendarEntry[];
  readonly now: Date;
}

@Component({
  selector: 'app-calendar-day-dialog',
  imports: [MatButtonModule, MatDialogModule, CalendarEntryCardWidget],
  templateUrl: './calendar-day-dialog.widget.html',
  styleUrl: './calendar-day-dialog.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarDayDialogWidget {
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialogRef: MatDialogRef<CalendarDayDialogWidget> = inject(MatDialogRef<CalendarDayDialogWidget>);
  private readonly dialog: MatDialog = inject(MatDialog);
  readonly data: CalendarDayDialogData = inject(MAT_DIALOG_DATA);

  protected readonly closeLabel: Signal<string> = computed((): string => this.i18n.t('common.close'));

  protected close(): void {
    this.dialogRef.close();
  }

  protected openEntryDetail(entry: CalendarEntry): void {
    const data: CalendarEntryDialogData = { entry, now: this.data.now };
    this.dialog.open(CalendarEntryDialogWidget, {
      width: '480px',
      maxWidth: '95vw',
      data,
    });
  }
}
