import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SizeUnitSystem } from '../interfaces/general/size-unit-system';
import { AppSettingsService } from './app-settings.service';
import { FileSizeService } from './file-size.service';

describe('FileSizeService', () => {
  it('updates computed sizes when units change and keeps exact bytes independent of units', () => {
    const sizeUnitSystem = signal<SizeUnitSystem>('Decimal');
    TestBed.configureTestingModule({
      providers: [{ provide: AppSettingsService, useValue: { sizeUnitSystem, localeId: () => 'de-DE' } }],
    });
    const service = TestBed.inject(FileSizeService);
    const formatted = computed(() => service.format(1024 ** 3, 'de-DE'));
    expect(formatted()).toBe('1,07 GB');
    expect(service.exact(1024 ** 3)).toBe('1.073.741.824 B');
    sizeUnitSystem.set('Binary');
    expect(formatted()).toBe('1 GiB');
    expect(service.exact(1024 ** 3)).toBe('1.073.741.824 B');
    expect(service.exact(0)).toBe('0 B');
    expect(service.exact(null)).toBeNull();
  });
});
