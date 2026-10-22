import { FormatBytesPipe } from './format-bytes.pipe';

describe('FormatBytesPipe', () => {
  const pipe: FormatBytesPipe = new FormatBytesPipe();

  it('should format small values as bytes', () => {
    expect(pipe.transform(0)).toBe('0 B');
    expect(pipe.transform(42)).toBe('42 B');
  });

  it('should scale values to larger units', () => {
    expect(pipe.transform(1024)).toBe('1.0 KB');
    expect(pipe.transform(1536)).toBe('1.5 KB');
    expect(pipe.transform(5 * 1024 * 1024)).toBe('5.0 MB');
  });

  it('should return zero bytes for invalid input', () => {
    expect(pipe.transform(Number.NaN)).toBe('0 B');
    expect(pipe.transform(-1)).toBe('0 B');
  });
});
