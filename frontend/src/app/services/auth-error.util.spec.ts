import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { AppI18nService } from './app-i18n.service';
import { AuthFailureKind, AuthRequestError, toAuthRequestError, translateAuthError } from './auth-error.util';

describe('auth-error.util', () => {
  const i18n: AppI18nService = new AppI18nService();

  beforeAll((): void => {
    i18n.setLanguage('en');
  });

  function problemResponse(body: Record<string, unknown>, status: number = 429, retryAfter: string | null = null): HttpErrorResponse {
    return new HttpErrorResponse({
      status,
      statusText: 'Too Many Requests',
      error: body,
      headers: retryAfter === null ? undefined : new HttpHeaders({ 'Retry-After': retryAfter }),
    });
  }

  /** The wait fields are the newest part of the contract, so they get their own readable name here. */
  function waitFieldsOf(error: unknown): { kind: AuthFailureKind | null; attempts: number | null; endsIn: number | null } {
    const requestError: AuthRequestError = error as AuthRequestError;
    const endsAt: number | null = requestError.retryEndsAt;

    return {
      kind: requestError.failureKind,
      attempts: requestError.remainingAttempts,
      endsIn: endsAt === null ? null : Math.round((endsAt - Date.now()) / 1000),
    };
  }

  it('should keep the stable problem code of a problem answer', () => {
    const error: AuthRequestError = toAuthRequestError(
      problemResponse({ detail: 'Nope.', code: 'account_locked' }, 429),
      'fallback message',
    );

    expect(error).toBeInstanceOf(AuthRequestError);
    expect(error.code).toBe('account_locked');
    expect(error.status).toBe(429);
  });

  it('should read which proof was refused and how many tries the account has left', () => {
    const error: AuthRequestError = toAuthRequestError(
      problemResponse({ detail: 'No.', code: 'invalid_credentials', failureKind: 'password', remainingAttempts: 2 }, 401),
      'fallback message',
    );

    expect(waitFieldsOf(error)).toEqual({ kind: 'password', attempts: 2, endsIn: null });
  });

  it('should anchor a relative wait at the moment the answer arrived', () => {
    const error: AuthRequestError = toAuthRequestError(
      problemResponse({ detail: 'Locked.', code: 'account_locked', remainingAttempts: 0, retryAfterSeconds: 300 }),
      'fallback message',
    );

    expect(waitFieldsOf(error).endsIn).toBe(300);
  });

  it('should prefer the answer header over the body when both name a wait', () => {
    const error: AuthRequestError = toAuthRequestError(
      problemResponse({ detail: 'Busy.', code: 'too_many_requests', retryAfterSeconds: 300 }, 429, '45'),
      'fallback message',
    );

    expect(waitFieldsOf(error).endsIn).toBe(45);
  });

  it('should keep the wait of a problem read out of a blob together with its answer header', () => {
    const decoded: Record<string, unknown> = { status: 429, detail: 'Locked.', code: 'account_locked', remainingAttempts: 0 };
    const error: AuthRequestError = toAuthRequestError(decoded, 'fallback message', '90');

    expect(error.code).toBe('account_locked');
    expect(waitFieldsOf(error).endsIn).toBe(90);
  });

  it('should fall back to the difference between the two instants when no seconds are claimed', () => {
    const error: AuthRequestError = toAuthRequestError(
      problemResponse({
        detail: 'Locked.',
        code: 'account_locked',
        retryAt: '2026-11-04T12:05:00Z',
        serverTime: '2026-11-04T12:00:00Z',
      }),
      'fallback message',
    );

    expect(waitFieldsOf(error).endsIn).toBe(300);
  });

  it('should leave the wait empty when an absolute instant stands alone without a server time', () => {
    const error: AuthRequestError = toAuthRequestError(
      problemResponse({ detail: 'Locked.', code: 'account_locked', retryAt: '2026-11-04T12:05:00Z' }),
      'fallback message',
    );

    expect(waitFieldsOf(error).endsIn).toBeNull();
  });

  for (const unusable of [0, -5, 90_000]) {
    it(`should treat ${unusable} claimed seconds as no usable wait`, () => {
      const error: AuthRequestError = toAuthRequestError(
        problemResponse({ detail: 'Locked.', code: 'account_locked', retryAfterSeconds: unusable }),
        'fallback message',
      );

      expect(waitFieldsOf(error).endsIn).toBeNull();
    });
  }

  it('should drop a failure kind the backend does not know', () => {
    const error: AuthRequestError = toAuthRequestError(
      problemResponse({ detail: 'No.', code: 'invalid_credentials', failureKind: 'something_else', remainingAttempts: 1 }, 401),
      'fallback message',
    );

    expect(waitFieldsOf(error)).toEqual({ kind: null, attempts: 1, endsIn: null });
  });

  it('should keep an answer that carries no problem document at all', () => {
    const error: AuthRequestError = toAuthRequestError(problemResponse({ message: 'gateway said no' }, 502), 'the fallback');

    expect(error.code).toBeNull();
    expect(error.retryEndsAt).toBeNull();
  });

  it('should hand an already converted error through untouched', () => {
    const original: AuthRequestError = new AuthRequestError('kept', 429, 'account_locked', {}, 'password', 0, 1_000);

    expect(toAuthRequestError(original, 'fallback message')).toBe(original);
  });

  describe('translateAuthError', () => {
    it('should name the refused proof and the tries that are left', () => {
      const error: AuthRequestError = new AuthRequestError('raw', 401, 'invalid_credentials', {}, 'password', 2);

      expect(translateAuthError(error, i18n)).toBe('The password is incorrect. 2 attempts left before the account is locked.');
    });

    it('should use the singular wording for the last try', () => {
      const error: AuthRequestError = new AuthRequestError('raw', 401, 'invalid_credentials', {}, 'second_factor', 1);

      expect(translateAuthError(error, i18n)).toBe('The confirmation code is incorrect. 1 attempt left before the account is locked.');
    });

    it('should name the account password for a refused backup proof', () => {
      const error: AuthRequestError = new AuthRequestError('raw', 401, 'backup_identity_verification_failed', {}, 'backup_password', 3);

      expect(translateAuthError(error, i18n)).toContain('Your account password is incorrect.');
    });

    it('should keep the lock wording alone when no try is left', () => {
      const error: AuthRequestError = new AuthRequestError('raw', 429, 'account_locked', {}, 'password', 0, 1_000);

      expect(translateAuthError(error, i18n)).toBe('Your account is locked after too many failed attempts.');
    });

    it('should translate the ip limit without an attempt count', () => {
      const error: AuthRequestError = new AuthRequestError('raw', 429, 'too_many_requests', {}, null, null, 45);

      expect(translateAuthError(error, i18n)).toBe('Too many requests from your IP address.');
    });

    it('should fall back to the server wording for an unknown problem code', () => {
      const error: AuthRequestError = new AuthRequestError('The gateway refused that.', 502, 'gateway_unhappy', {});

      expect(translateAuthError(error, i18n)).toBe('The gateway refused that.');
    });
  });
});
