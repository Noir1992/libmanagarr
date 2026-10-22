import { formatDate } from '@angular/common';
import { Pipe, PipeTransform, inject } from '@angular/core';
import { AppSettingsService } from '../services/app-settings.service';

@Pipe({
  name: 'appDate',
  pure: true,
})
export class AppDatePipe implements PipeTransform {
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);

  transform(value: string | null, format: string = 'mediumDate'): string {
    if (value === null) {
      return '-';
    }

    return formatDate(value, format, this.appSettingsService.localeId());
  }
}
