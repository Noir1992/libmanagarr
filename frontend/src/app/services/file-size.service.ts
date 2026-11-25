import { inject, Injectable } from '@angular/core';
import { FormatBytesPipe } from '../pipes/format-bytes.pipe';
import { AppSettingsService } from './app-settings.service';

@Injectable({ providedIn: 'root' })
export class FileSizeService {
  private readonly settings = inject(AppSettingsService);
  private readonly pipe = new FormatBytesPipe();

  format(bytes: number, locale?: string): string {
    return this.pipe.transform(bytes, locale, this.settings.sizeUnitSystem());
  }

  localized(bytes: number): string {
    return this.format(bytes, this.settings.localeId());
  }

  exact(bytes: number | null | undefined): string | null {
    return bytes == null ? null : `${bytes.toLocaleString(this.settings.localeId())} B`;
  }
}
