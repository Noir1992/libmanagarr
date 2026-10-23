import { ChangeDetectionStrategy, Component, computed, inject, input, InputSignal, output, OutputEmitterRef, Signal } from '@angular/core';
import { MatNativeDateModule } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTimepicker, MatTimepickerInput, MatTimepickerToggle } from '@angular/material/timepicker';
import { AppI18nService } from '../../services/app-i18n.service';

export type DateRangeTimeValue = {
  startDate: Date | null;
  endDate: Date | null;
};

@Component({
  selector: 'app-date-range-time-picker',
  imports: [
    MatDatepickerModule,
    MatNativeDateModule,
    MatFormFieldModule,
    MatInputModule,
    MatTimepicker,
    MatTimepickerToggle,
    MatTimepickerInput,
  ],
  templateUrl: './date-range-time-picker.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './date-range-time-picker.widget.scss',
})
export class DateRangeTimePickerWidget {
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  readonly label: InputSignal<string> = input.required<string>();
  readonly startDate: InputSignal<Date | null> = input.required<Date | null>();
  readonly endDate: InputSignal<Date | null> = input.required<Date | null>();
  readonly rangeChange: OutputEmitterRef<DateRangeTimeValue> = output<DateRangeTimeValue>();

  readonly dateLabel: Signal<string> = computed((): string => this.i18n.t('common.date'));
  readonly timeFromLabel: Signal<string> = computed((): string => this.i18n.t('common.fromTime'));
  readonly timeToLabel: Signal<string> = computed((): string => this.i18n.t('common.toTime'));

  protected readonly startDateValue: Signal<Date | null> = computed(() => this.startDate());
  protected readonly endDateValue: Signal<Date | null> = computed(() => this.endDate());

  protected readonly startTimeValue: Signal<Date | null> = computed(() => this.extractTime(this.startDate()));
  protected readonly endTimeValue: Signal<Date | null> = computed(() => this.extractTime(this.endDate()));

  protected onDateChanged(boundary: 'start' | 'end', value: Date | null): void {
    if (value === null || Number.isNaN(value.getTime())) {
      return;
    }

    const baseDate: Date = new Date(value.getFullYear(), value.getMonth(), value.getDate());
    const sourceDate: Date | null = boundary === 'start' ? this.startDate() : this.endDate();

    const mergedDate: Date = this.mergeDateAndTime(baseDate, sourceDate);

    if (mergedDate?.getTime() === sourceDate?.getTime()) {
      return;
    }

    if (boundary === 'start') {
      this.rangeChange.emit({ startDate: mergedDate, endDate: this.endDate() });
    } else {
      this.rangeChange.emit({ startDate: this.startDate(), endDate: mergedDate });
    }
  }

  protected onTimeChanged(boundary: 'start' | 'end', value: Date | null): void {
    const sourceDate: Date | null = boundary === 'start' ? this.startDate() : this.endDate();
    const sourceOnlyDate: Date | null = sourceDate ? this.stripTime(sourceDate) : null;

    const mergedDate: Date | null = sourceOnlyDate ? this.mergeDateAndTime(sourceOnlyDate, value) : null;

    if (mergedDate?.getTime() === sourceDate?.getTime()) {
      return;
    }

    if (boundary === 'start') {
      this.rangeChange.emit({ startDate: mergedDate, endDate: this.endDate() });
    } else {
      this.rangeChange.emit({ startDate: this.startDate(), endDate: mergedDate });
    }
  }

  private extractTime(date: Date | null): Date | null {
    if (date === null || Number.isNaN(date.getTime())) {
      return null;
    }

    const result: Date = new Date();
    result.setHours(date.getHours(), date.getMinutes(), date.getSeconds(), date.getMilliseconds());
    return result;
  }

  private stripTime(date: Date): Date {
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
  }

  private mergeDateAndTime(datePart: Date | null, timePart: Date | null): Date {
    if (datePart === null) {
      return timePart ?? new Date();
    }

    const result: Date = new Date(datePart);

    if (timePart !== null && !Number.isNaN(timePart.getTime())) {
      result.setHours(timePart.getHours(), timePart.getMinutes(), timePart.getSeconds(), timePart.getMilliseconds());
    }

    return result;
  }
}
