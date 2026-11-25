import { FormatBytesPipe } from './format-bytes.pipe';

describe('FormatBytesPipe', () => {
  const pipe: FormatBytesPipe = new FormatBytesPipe();

  it('should format small values as bytes', () => {
    expect(pipe.transform(0)).toBe('0 B');
    expect(pipe.transform(42)).toBe('42 B');
  });

  it('should scale values to larger units', () => {
    expect(pipe.transform(1000)).toBe('1.0 KB');
    expect(pipe.transform(1500)).toBe('1.5 KB');
    expect(pipe.transform(5 * 1000 * 1000)).toBe('5.0 MB');
    expect(pipe.transform(2.5 * 1000 ** 3)).toBe('2.5 GB');
    expect(pipe.transform(2.5 * 1000 ** 4)).toBe('2.5 TB');
  });

  it('should use decimal units for binary-sized input', () => {
    expect(pipe.transform(1024 ** 3, 'en-US')).toBe('1.07 GB');
    expect(pipe.transform(999)).toBe('999 B');
    expect(pipe.transform(1000 ** 2)).toBe('1.0 MB');
    expect(pipe.transform(1000 ** 3)).toBe('1.0 GB');
    expect(pipe.transform(1000 ** 4)).toBe('1.0 TB');
  });

  it('should preserve localized detail values with the same units', () => {
    expect(pipe.transform(112.64 * 1000 ** 2, 'en-US')).toBe('112.64 MB');
    expect(pipe.transform(112.64 * 1000 ** 2, 'de-DE')).toBe('112,64 MB');
    expect(pipe.transform(2.5 * 1000 ** 4, 'de-DE')).toBe('2,5 TB');
    expect(pipe.transform(0, 'de-DE')).toBe('0 B');
  });

  it('uses binary units only with the matching base', () => {
    expect(pipe.transform(1023, 'en-US', 'Binary')).toBe('1,023 B');
    expect(pipe.transform(1024, 'en-US', 'Binary')).toBe('1 KiB');
    expect(pipe.transform(1024 ** 2, 'en-US', 'Binary')).toBe('1 MiB');
    expect(pipe.transform(1024 ** 3, 'en-US', 'Binary')).toBe('1 GiB');
    expect(pipe.transform(2.5 * 1024 ** 4, 'de-DE', 'Binary')).toBe('2,5 TiB');
    expect(pipe.transform(1000 ** 3, 'en-US', 'Binary')).toBe('953.67 MiB');
    expect(pipe.transform(0, 'en-US', 'Binary')).toBe('0 B');
  });

  it('should return zero bytes for invalid input', () => {
    expect(pipe.transform(Number.NaN)).toBe('0 B');
    expect(pipe.transform(-1)).toBe('0 B');
  });
});
