import { TestBed } from '@angular/core/testing';
import { AuthRequestError } from './auth-error.util';
import { AuthWaitKind, AuthWaitService } from './auth-wait.service';

describe('AuthWaitService', () => {
  let service: AuthWaitService;

  beforeEach(() => {
    TestBed.configureTestingModule({});

    service = TestBed.inject(AuthWaitService);
  });

  /** Builds the refusal a surface would hand over, with the wait ending `seconds` from now. */
  function refusal(code: string, seconds: number | null): AuthRequestError {
    return new AuthRequestError(
      'refused',
      429,
      code,
      {},
      code === 'account_locked' ? 'password' : null,
      code === 'account_locked' ? 0 : null,
      seconds === null ? null : Date.now() + seconds * 1000,
    );
  }

  function countdownOf(kind: AuthWaitKind): string | null {
    return service.countdownOf(kind);
  }

  /** The instant every wait of this suite is measured from. */
  const clockBase: Date = new Date('2026-11-04T12:00:00Z');

  /**
   * Runs a body with the clock held at `clockBase`. Building a wait and recording it read the clock
   * once each, and a countdown is floored, so a single millisecond between those two reads would
   * otherwise shorten the exact text by a second — which the runner decides, not the code.
   */
  function withFrozenClock(body: () => void): void {
    jasmine.clock().install();
    jasmine.clock().mockDate(clockBase);

    try {
      body();
    } finally {
      // Stops the ticking interval through the faked clearInterval, so the real one is never handed
      // an id of the mock clock after the uninstall below.
      service.clear();
      jasmine.clock().uninstall();
    }
  }

  it('should hand the very error back so a caller records without changing its throw', () => {
    const error: AuthRequestError = refusal('account_locked', 60);

    expect(service.record(error)).toBe(error);
  });

  it('should know nothing about a wait at the start', () => {
    expect(service.waiting()).toBeFalse();
    expect(service.accountLocked()).toBeFalse();
    expect(countdownOf('account_lock')).toBeNull();
    expect(countdownOf('ip_limit')).toBeNull();
  });

  it('should keep a locked account apart from a busy ip address', () => {
    withFrozenClock((): void => {
      service.record(refusal('account_locked', 45));

      expect(service.accountLocked()).toBeTrue();
      expect(countdownOf('account_lock')).toBe('00:45');
      expect(countdownOf('ip_limit')).toBeNull();
    });
  });

  it('should remember both waits at once', () => {
    withFrozenClock((): void => {
      service.record(refusal('account_locked', 120));
      service.record(refusal('too_many_requests', 30));

      expect(service.accountLocked()).toBeTrue();
      expect(countdownOf('account_lock')).toBe('02:00');
      expect(countdownOf('ip_limit')).toBe('00:30');
    });
  });

  it('should report waiting while either wait runs', () => {
    service.record(refusal('too_many_requests', 20));

    expect(service.waiting()).toBeTrue();
    expect(service.accountLocked()).toBeFalse();
  });

  it('should stop waiting when a recorded deadline already passed', () => {
    service.record(refusal('account_locked', 60));
    service.record(refusal('account_locked', -1));

    expect(countdownOf('account_lock')).toBeNull();
    expect(service.waiting()).toBeFalse();
  });

  it('should forget every wait on request', () => {
    service.record(refusal('account_locked', 60));
    service.record(refusal('too_many_requests', 60));

    service.clear();

    expect(service.waiting()).toBeFalse();
    expect(service.endsAtOf('account_lock')).toBeNull();
  });

  it('should keep no wait for a refusal that names neither limit', () => {
    service.record(refusal('invalid_credentials', 60));

    expect(service.waiting()).toBeFalse();
  });

  it('should keep no wait for a refusal that carries no usable timing', () => {
    service.record(refusal('account_locked', null));

    expect(service.waiting()).toBeFalse();
    expect(service.endsAtOf('account_lock')).toBeNull();
  });

  it('should hand the end of a wait to the surface as the instant it runs to', () => {
    service.record(refusal('account_locked', 300));

    const endsAt: string | null = service.endsAtOf('account_lock');

    expect(endsAt).not.toBeNull();
    expect(Date.parse(endsAt as string)).toBeGreaterThan(Date.now());
  });

  it('should advance the countdown with the clock and let go once it ran out', () => {
    withFrozenClock((): void => {
      service.record(refusal('account_locked', 3));
      expect(countdownOf('account_lock')).toBe('00:03');

      jasmine.clock().tick(1000);
      expect(countdownOf('account_lock')).toBe('00:02');

      jasmine.clock().tick(3000);
      expect(countdownOf('account_lock')).toBeNull();
      expect(service.waiting()).toBeFalse();
    });
  });
});
