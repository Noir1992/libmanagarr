import { HttpErrorResponse } from '@angular/common/http';
import { AppI18nService } from './app-i18n.service';
import { TranslationKey } from './i18n/_i18n-key';

interface ProblemPayload {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/**
 * Transport-independent error for every auth and account endpoint. The stable ProblemDetails
 * `code` extension and the validation field codes stay intact so each surface can translate
 * the failure itself instead of parsing human-readable text.
 */
export class AuthRequestError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code: string | null,
    readonly fieldErrors: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'AuthRequestError';
  }
}

const AUTH_ERROR_KEYS: Readonly<Record<string, TranslationKey>> = {
  security_state_changed: 'auth.securityStateChanged',
  invalid_credentials: 'auth.invalidCredentials',
  account_locked: 'auth.accountLocked',
  too_many_requests: 'auth.rateLimited',
  setup_not_available: 'auth.setupUnavailable',
  current_password_incorrect: 'auth.currentPasswordIncorrect',
  second_factor_required: 'auth.secondFactorRequired',
  invalid_two_factor_code: 'auth.invalidCode',
  two_factor_challenge_invalid: 'auth.expiredChallenge',
  two_factor_setup_invalid: 'auth.twoFactorSetupInvalid',
  two_factor_already_enabled: 'auth.twoFactorAlreadyEnabled',
  two_factor_not_enabled: 'auth.twoFactorNotEnabled',
};

const AUTH_FIELD_ERROR_KEYS: Readonly<Record<string, TranslationKey>> = {
  username_required: 'auth.usernameRequired',
  username_length: 'auth.usernameLength',
  password_required: 'auth.passwordRequired',
  password_length: 'auth.passwordLength',
  password_confirmation_mismatch: 'auth.passwordMismatch',
  password_reuse: 'auth.passwordReuse',
  current_password_required: 'auth.currentPasswordRequired',
};

export function toAuthRequestError(error: unknown, fallbackMessage: string): AuthRequestError {
  if (error instanceof AuthRequestError) {
    return error;
  }

  if (error instanceof HttpErrorResponse) {
    const problem: ProblemPayload | null = readProblemPayload(error.error);

    if (problem !== null) {
      return new AuthRequestError(
        problem.detail ?? error.message ?? fallbackMessage,
        problem.status ?? error.status,
        problem.code ?? null,
        problem.errors ?? {},
      );
    }

    return new AuthRequestError(error.message ?? fallbackMessage, error.status, null, {});
  }

  // A caller that reads a blob body itself hands over the decoded document, which is neither a
  // response nor an error object and would otherwise lose the problem code it carries.
  const decodedProblem: ProblemPayload | null = readProblemPayload(error);

  if (decodedProblem !== null) {
    return new AuthRequestError(
      decodedProblem.detail ?? decodedProblem.title ?? fallbackMessage,
      decodedProblem.status ?? 0,
      decodedProblem.code ?? null,
      decodedProblem.errors ?? {},
    );
  }

  return new AuthRequestError(error instanceof Error && error.message.trim().length > 0 ? error.message : fallbackMessage, 0, null, {});
}

/** Translates a stable problem code into the user language, falling back to the server detail. */
export function translateAuthError(error: AuthRequestError, i18n: AppI18nService): string {
  const key: TranslationKey | undefined = error.code === null ? undefined : AUTH_ERROR_KEYS[error.code];

  return key !== undefined ? i18n.t(key) : error.message;
}

/** Translates one validation field code into the user language, or keeps the raw code. */
export function translateAuthFieldError(code: string, i18n: AppI18nService): string {
  const key: TranslationKey | undefined = AUTH_FIELD_ERROR_KEYS[code];

  return key !== undefined ? i18n.t(key) : code;
}

function readProblemPayload(payload: unknown): ProblemPayload | null {
  if (typeof payload !== 'object' || payload === null) {
    return null;
  }

  const record: Partial<ProblemPayload> = payload as Partial<ProblemPayload>;

  if (record.detail === undefined && record.title === undefined && record.code === undefined && record.errors === undefined) {
    return null;
  }

  return {
    status: typeof record.status === 'number' ? record.status : undefined,
    title: typeof record.title === 'string' ? record.title : undefined,
    detail: typeof record.detail === 'string' ? record.detail : undefined,
    code: typeof record.code === 'string' ? record.code : undefined,
    errors: typeof record.errors === 'object' && record.errors !== null ? (record.errors as Record<string, string[]>) : undefined,
  };
}
