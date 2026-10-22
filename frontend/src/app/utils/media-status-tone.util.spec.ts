import { airStatusTone, fileStatusTone } from './media-status-tone.util';

describe('media status tones', () => {
  it('maps completed air statuses to success regardless of casing', () => {
    expect(airStatusTone('released')).toBe('success');
    expect(airStatusTone('ended')).toBe('success');
    expect(airStatusTone('Released')).toBe('success');
    expect(airStatusTone('ENDED')).toBe('success');
  });

  it('maps continuing to info', () => {
    expect(airStatusTone('continuing')).toBe('info');
  });

  it('maps pending air statuses to warning regardless of casing', () => {
    for (const status of ['announced', 'inCinemas', 'physicalRelease', 'digitalRelease', 'upcoming', 'tba']) {
      expect(airStatusTone(status)).withContext(status).toBe('warning');
    }
  });

  it('maps deleted to danger', () => {
    expect(airStatusTone('deleted')).toBe('danger');
  });

  it('returns no tone for unknown or missing air statuses', () => {
    expect(airStatusTone('somethingelse')).toBeUndefined();
    expect(airStatusTone(undefined)).toBeUndefined();
  });

  it('maps availability to success or danger and keeps unknown uncolored', () => {
    expect(fileStatusTone(true)).toBe('success');
    expect(fileStatusTone(false)).toBe('danger');
    expect(fileStatusTone(undefined)).toBeUndefined();
  });
});
