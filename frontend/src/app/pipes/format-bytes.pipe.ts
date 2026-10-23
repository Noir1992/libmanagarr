import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'formatBytes',
  pure: true,
})
export class FormatBytesPipe implements PipeTransform {
  transform(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return '0 B';
    }

    const units: readonly string[] = ['B', 'KB', 'MB', 'GB', 'TB'];
    let value: number = bytes;
    let unitIndex: number = 0;

    while (value >= 1024 && unitIndex < units.length - 1) {
      value /= 1024;
      unitIndex++;
    }

    const decimals: number = value >= 100 || unitIndex === 0 ? 0 : 1;
    return `${value.toFixed(decimals)} ${units[unitIndex]}`;
  }
}
