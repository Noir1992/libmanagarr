import { TestBed } from '@angular/core/testing';
import { AppI18nService } from '../services/app-i18n.service';
import { airStatusLabel } from './air-status-label.util';

describe('airStatusLabel', () => {
  let i18n: AppI18nService;

  beforeEach(() => {
    i18n = TestBed.inject(AppI18nService);
    i18n.setLanguage('en');
  });

  it('labels every known air status instead of showing the raw token', () => {
    const expected: Record<string, string> = {
      announced: 'Announced',
      continuing: 'Continuing',
      deleted: 'Deleted',
      digitalRelease: 'Digital release',
      ended: 'Ended',
      inCinemas: 'In cinemas',
      physicalRelease: 'Physical release',
      released: 'Released',
      tba: 'TBA',
      upcoming: 'Upcoming',
      Unknown: 'Unknown',
    };

    for (const [status, label] of Object.entries(expected)) {
      expect(airStatusLabel(status, i18n)).withContext(status).toBe(label);
    }
  });

  it('labels the same status regardless of the casing the instance used', () => {
    expect(airStatusLabel('inCinemas', i18n)).toBe(airStatusLabel('incinemas', i18n));
    expect(airStatusLabel('CONTINUING', i18n)).toBe('Continuing');
  });

  it('translates the label into the selected language', () => {
    i18n.setLanguage('de');
    expect(airStatusLabel('continuing', i18n)).toBe('Laufend');
    expect(airStatusLabel('released', i18n)).toBe('Erschienen');
  });

  it('keeps a status the instances introduced later as it arrived', () => {
    expect(airStatusLabel('streamingSoon', i18n)).toBe('streamingSoon');
  });

  it('shows the not-available label for a missing status', () => {
    expect(airStatusLabel(undefined, i18n)).toBe('N/A');
    expect(airStatusLabel('', i18n)).toBe('N/A');
    expect(airStatusLabel('   ', i18n)).toBe('N/A');
  });
});
