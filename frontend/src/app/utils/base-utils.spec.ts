import { normalizeBaseUrl, hashColor } from './base-utils';

describe('normalizeBaseUrl', () => {
  it('removes trailing slashes', () => {
    expect(normalizeBaseUrl('http://localhost:8989/')).toBe('http://localhost:8989');
    expect(normalizeBaseUrl('http://localhost:7878////')).toBe('http://localhost:7878');
  });

  it('keeps urls without trailing slashes unchanged', () => {
    expect(normalizeBaseUrl('http://localhost:8989')).toBe('http://localhost:8989');
  });
});

describe('hashColor', () => {
  it('returns a valid HSL color string', () => {
    const color = hashColor('test');
    expect(color).toMatch(/^hsl\(\d+, 45%, 28%\)$/);
  });

  it('returns consistent colors for the same input', () => {
    expect(hashColor('Matrix')).toBe(hashColor('Matrix'));
    expect(hashColor('Inception')).toBe(hashColor('Inception'));
  });

  it('returns different colors for different inputs', () => {
    expect(hashColor('Matrix')).not.toBe(hashColor('Inception'));
  });

  it('returns hue between 0 and 359', () => {
    const color = hashColor('some input');
    const hue = parseInt(color.match(/hsl\((\d+)/)![1], 10);
    expect(hue).toBeGreaterThanOrEqual(0);
    expect(hue).toBeLessThanOrEqual(359);
  });
});
