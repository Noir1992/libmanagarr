import { SizeUnitSystem } from '../interfaces/general/size-unit-system';
import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'formatBytes',
  pure: true,
})
export class FormatBytesPipe implements PipeTransform {
  transform(bytes: number, locale?: string, unitSystem: SizeUnitSystem = 'Decimal'): string {
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return '0 B';
    }

    const binary = unitSystem === 'Binary';
    const base = binary ? 1024 : 1000;
    const units: readonly string[] = binary ? ['B', 'KiB', 'MiB', 'GiB', 'TiB'] : ['B', 'KB', 'MB', 'GB', 'TB'];
    let value: number = bytes;
    let unitIndex: number = 0;

    while (value >= base && unitIndex < units.length - 1) {
      value /= base;
      unitIndex++;
    }

    const decimals: number = value >= 100 || unitIndex === 0 ? 0 : 1;
    const formattedValue = locale ? value.toLocaleString(locale, { maximumFractionDigits: 2 }) : value.toFixed(decimals);
    return `${formattedValue} ${units[unitIndex]}`;
  }
}
