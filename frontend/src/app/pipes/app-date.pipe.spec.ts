import { TestBed } from '@angular/core/testing';
import { AppDatePipe } from './app-date.pipe';
import { AppSettingsService } from '../services/app-settings.service';

describe('AppDatePipe', () => {
  let pipe: AppDatePipe;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AppDatePipe,
        {
          provide: AppSettingsService,
          useValue: {
            localeId: (): string => 'en-US',
          },
        },
      ],
    });

    pipe = TestBed.inject(AppDatePipe);
  });

  it('should format dates using the app locale', () => {
    expect(pipe.transform('2024-01-15T12:34:56Z', 'mediumDate')).toContain('2024');
  });

  it('should return a dash for missing values', () => {
    expect(pipe.transform(null)).toBe('-');
  });
});
