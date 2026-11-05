import { HttpErrorResponse } from '@angular/common/http';
import { AppI18nService } from './app-i18n.service';
import { TranslationKey } from './i18n/_i18n-key';

/** Which proof a refusal rejected, exactly as the backend names it. */
export type AuthFailureKind = 'password' | 'second_factor' | 'backup_password';

interface ProblemPayload {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
  failureKind?: string;
  remainingAttempts?: number;
  retryAfterSeconds?: number;
  retryAt?: string;
  serverTime?: string;
}

/**
 * Transport-independent error for every auth and account endpoint. The stable ProblemDetails
 * `code` extension and the validation field codes stay intact so each surface can translate
 * the failure itself instead of parsing human-readable text.
 *
 * The three wait fields hold what a refusal says about the limits: which proof was wrong, how
 * many tries the account has left, and when the caller may send again. They are validated while
 * reading, so an answer without usable timing leaves them empty instead of starting a countdown
 * that never ends.
 */
export class AuthRequestError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code: string | null,
    readonly fieldErrors: Record<string, string[]>,
    readonly failureKind: AuthFailureKind | null = null,
    readonly remainingAttempts: number | null = null,
    readonly retryEndsAt: number | null = null,
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

/**
 * The codes that end the attempt instead of refusing one proof. From here the limit is the reason,
 * so its wording stands alone even when the refusal still names the proof that went wrong.
 */
const WAIT_PROBLEM_CODES: ReadonlySet<string> = new Set(['account_locked', 'too_many_requests']);

/** A refusal that named the proof it checked can say so, which is kinder than doubting the password. */
const PROOF_FAILURE_KEYS: Readonly<Record<string, TranslationKey>> = {
  password: 'auth.passwordIsWrong',
  second_factor: 'auth.codeIsWrong',
  backup_password: 'auth.accountPasswordIsWrong',
};

const FAILURE_KINDS: readonly AuthFailureKind[] = ['password', 'second_factor', 'backup_password'];

const AUTH_FIELD_ERROR_KEYS: Readonly<Record<string, TranslationKey>> = {
  username_required: 'auth.usernameRequired',
  username_length: 'auth.usernameLength',
  password_required: 'auth.passwordRequired',
  password_length: 'auth.passwordLength',
  password_confirmation_mismatch: 'auth.passwordMismatch',
  password_reuse: 'auth.passwordReuse',
  current_password_required: 'auth.currentPasswordRequired',
};

/**
 * The longest wait the countdown will show. The fixed windows of the backend stay far below it, so
 * anything longer is a broken timing claim and belongs in the general refusal instead of a clock
 * the user has to watch for hours.
 */
const MAXIMUM_COUNTDOWN_SECONDS: number = 15 * 60;

/**
 * Turns anything a call threw into the error the forms translate. `retryAfterHeader` carries the
 * answer header for the callers that read a problem document out of a blob themselves, where the
 * response object is already gone by the time the failure is built.
 */
export function toAuthRequestError(error: unknown, fallbackMessage: string, retryAfterHeader: string | null = null): AuthRequestError {
  if (error instanceof AuthRequestError) {
    return error;
  }

  if (error instanceof HttpErrorResponse) {
    const problem: ProblemPayload | null = readProblemPayload(error.error);

    if (problem !== null) {
      return toProblemError(
        problem,
        error.message ?? fallbackMessage,
        error.status,
        readRetryAfterHeader(error.headers.get('Retry-After')),
      );
    }

    return new AuthRequestError(error.message ?? fallbackMessage, error.status, null, {});
  }

  // A caller that reads a blob body itself hands over the decoded document, which is neither a
  // response nor an error object and would otherwise lose the problem code it carries.
  const decodedProblem: ProblemPayload | null = readProblemPayload(error);

  if (decodedProblem !== null) {
    return toProblemError(
      decodedProblem,
      decodedProblem.detail ?? decodedProblem.title ?? fallbackMessage,
      decodedProblem.status ?? 0,
      readRetryAfterHeader(retryAfterHeader),
    );
  }

  return new AuthRequestError(error instanceof Error && error.message.trim().length > 0 ? error.message : fallbackMessage, 0, null, {});
}

function toProblemError(problem: ProblemPayload, message: string, status: number, retryAfterHeader: number | null): AuthRequestError {
  return new AuthRequestError(
    message,
    status,
    problem.code ?? null,
    problem.errors ?? {},
    readFailureKind(problem.failureKind),
    readRemainingAttempts(problem.remainingAttempts),
    readRetryEndsAt(problem, retryAfterHeader),
  );
}

/** Translates a stable problem code into the user language, falling back to the server detail. */
export function translateAuthError(error: AuthRequestError, i18n: AppI18nService): string {
  const base: string = baseMessageOf(error, i18n);

  // A lock announces itself with zero tries left, and counting down from zero would read as a bug.
  if (error.remainingAttempts === null || error.remainingAttempts === 0 || error.failureKind === null) {
    return base;
  }

  const warning: TranslationKey =
    error.remainingAttempts === 1 ? PROOF_WARNING_KEYS[error.failureKind].one : PROOF_WARNING_KEYS[error.failureKind].many;

  return `${base} ${i18n.t(warning, { count: error.remainingAttempts.toString() })}`;
}

/**
 * The first sentence of a refusal. A disclosed attempt count is only ever sent for an account that
 * exists, so from there the wording names the proof the caller should fix. A limit code names the
 * limit instead, because at that point no proof is worth fixing any more.
 */
function baseMessageOf(error: AuthRequestError, i18n: AppI18nService): string {
  const proofKey: TranslationKey | undefined = error.failureKind === null ? undefined : PROOF_FAILURE_KEYS[error.failureKind];
  const key: TranslationKey | undefined = error.code === null ? undefined : AUTH_ERROR_KEYS[error.code];
  const isWait: boolean = error.code !== null && WAIT_PROBLEM_CODES.has(error.code);

  if (isWait !== true && error.remainingAttempts !== null && proofKey !== undefined) {
    return i18n.t(proofKey);
  }

  return key !== undefined ? i18n.t(key) : error.message;
}

const PROOF_WARNING_KEYS: Readonly<Record<AuthFailureKind, { readonly one: TranslationKey; readonly many: TranslationKey }>> = {
  password: { one: 'auth.passwordAttemptsOne', many: 'auth.passwordAttempts' },
  second_factor: { one: 'auth.codeAttemptsOne', many: 'auth.codeAttempts' },
  backup_password: { one: 'auth.accountPasswordAttemptsOne', many: 'auth.accountPasswordAttempts' },
};

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
    failureKind: typeof record.failureKind === 'string' ? record.failureKind : undefined,
    remainingAttempts: typeof record.remainingAttempts === 'number' ? record.remainingAttempts : undefined,
    retryAfterSeconds: typeof record.retryAfterSeconds === 'number' ? record.retryAfterSeconds : undefined,
    retryAt: typeof record.retryAt === 'string' ? record.retryAt : undefined,
    serverTime: typeof record.serverTime === 'string' ? record.serverTime : undefined,
  };
}

function readFailureKind(value: string | undefined): AuthFailureKind | null {
  if (value === undefined) {
    return null;
  }

  return FAILURE_KINDS.find((kind: AuthFailureKind): boolean => kind === value) ?? null;
}

function readRemainingAttempts(value: number | undefined): number | null {
  if (value === undefined || !Number.isInteger(value) || value < 0) {
    return null;
  }

  return value;
}

/**
 * The local instant the wait runs to, or nothing the caller can build a countdown from. A relative
 * claim is anchored at the moment the answer arrived, so the browser clock never races the server;
 * only when an answer carries no seconds at all does the absolute instant stand in.
 */
function readRetryEndsAt(problem: ProblemPayload, retryAfterHeader: number | null): number | null {
  const receivedAt: number = Date.now();

  if (retryAfterHeader !== null) {
    return receivedAt + retryAfterHeader * 1000;
  }

  const seconds: number | null = readWaitSeconds(problem.retryAfterSeconds);

  if (seconds !== null) {
    return receivedAt + seconds * 1000;
  }

  const retryAt: number = readInstant(problem.retryAt);

  if (retryAt === -1) {
    return null;
  }

  const serverTime: number = readInstant(problem.serverTime);
  const claimedSeconds: number = serverTime === -1 ? Number.POSITIVE_INFINITY : Math.ceil((retryAt - serverTime) / 1000);

  return claimedSeconds >= 1 && claimedSeconds <= MAXIMUM_COUNTDOWN_SECONDS ? receivedAt + claimedSeconds * 1000 : null;
}

function readWaitSeconds(value: number | undefined): number | null {
  if (value === undefined || !Number.isFinite(value)) {
    return null;
  }

  const seconds: number = Math.ceil(value);

  return seconds >= 1 && seconds <= MAXIMUM_COUNTDOWN_SECONDS ? seconds : null;
}

function readInstant(value: string | undefined): number {
  if (value === undefined) {
    return -1;
  }

  const instant: number = Date.parse(value);

  return Number.isNaN(instant) ? -1 : instant;
}

/** The `Retry-After` answer in seconds, whether it arrived as a delay or as an absolute date. */
function readRetryAfterHeader(value: string | null): number | null {
  if (value === null || value.trim().length === 0) {
    return null;
  }

  const seconds: number = Number(value.trim());

  if (Number.isFinite(seconds)) {
    return readWaitSeconds(seconds);
  }

  const instant: number = readInstant(value);

  if (instant === -1) {
    return null;
  }

  return readWaitSeconds(Math.ceil((instant - Date.now()) / 1000));
}
