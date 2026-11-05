import { computed, DestroyRef, inject, Injectable, NgZone, Signal, signal, WritableSignal } from '@angular/core';
import { AuthRequestError } from './auth-error.util';

/** The waiting kind a refusal announced. Both come back as 429, so the problem code is what tells them apart. */
export type AuthWaitKind = 'account_lock' | 'ip_limit';

/**
 * Which waiting kind a problem code announced. The backend sends both limits as 429, so the code is
 * the only thing that tells an account lock from a busy ip address, and it never changes with the
 * wording of the answer.
 */
const WAIT_KIND_BY_CODE: Readonly<Record<string, AuthWaitKind>> = {
  account_locked: 'account_lock',
  too_many_requests: 'ip_limit',
};

/**
 * The waits a refusal announced, kept for the session instead of for the surface that received
 * them. A lock belongs to the account and an ip limit to the browser, so both stay known while the
 * user moves between the sign-in, setup, account and backup screens; reloading forgets them, which
 * is only ever a shorter wait, never an extra attempt.
 *
 * Both deadlines are absolute instants on the local clock, so a throttled background tab, a missed
 * timer or a clock jump can delay the next tick but cannot extend or invent a wait. An instant that
 * already passed reads as no wait everywhere at once, which is what re-enables the actions.
 */
@Injectable({ providedIn: 'root' })
export class AuthWaitService {
  /* Injections */
  private readonly zone: NgZone = inject(NgZone);
  private readonly destroyRef: DestroyRef = inject(DestroyRef);

  /* Signals */
  private readonly waitEndsAtSignal: WritableSignal<Partial<Record<AuthWaitKind, number>>> = signal<Partial<Record<AuthWaitKind, number>>>(
    {},
  );
  private readonly nowSignal: WritableSignal<number> = signal<number>(Date.now());

  /** Set while a wait runs and taken back the moment both lapsed, so an idle session holds no timer. */
  private timerId?: number;

  /** Whether an action that needs a proof has to wait. Only the buttons react; the fields stay editable. */
  readonly waiting: Signal<boolean> = computed(
    (): boolean => this.countdownOf('account_lock') !== null || this.countdownOf('ip_limit') !== null,
  );

  /** True while the account itself is locked, which is the one wait the setup screen cannot hit. */
  readonly accountLocked: Signal<boolean> = computed((): boolean => this.countdownOf('account_lock') !== null);

  /* Lifecycle */
  constructor() {
    const visibilityListener: () => void = (): void => {
      if (globalThis.document.visibilityState === 'visible') {
        this.refresh();
      }
    };

    globalThis.document.addEventListener('visibilitychange', visibilityListener);
    this.destroyRef.onDestroy((): void => {
      globalThis.document.removeEventListener('visibilitychange', visibilityListener);
      this.stopTicking();
    });
  }

  /**
   * Remembers what a refusal said about the limits and hands the error back unchanged, so every
   * caller records by wrapping the throw it already performs. An answer without usable timing is
   * not a wait: the caller keeps its general refusal and the actions stay clickable.
   */
  record(error: AuthRequestError): AuthRequestError {
    const kind: AuthWaitKind | undefined = error.code === null ? undefined : WAIT_KIND_BY_CODE[error.code];

    if (kind !== undefined && error.retryEndsAt !== null) {
      const next: Partial<Record<AuthWaitKind, number>> = { ...this.waitEndsAtSignal(), [kind]: error.retryEndsAt };
      this.waitEndsAtSignal.set(next);
    }

    this.refresh();
    return error;
  }

  /** Forgets every known wait, which is what a signed-in session means for the limits. */
  clear(): void {
    this.waitEndsAtSignal.set({});
    this.refresh();
  }

  /** The countdown of one waiting kind as `mm:ss`, or null while that wait is unknown or over. */
  countdownOf(kind: AuthWaitKind): string | null {
    const endsAt: number | undefined = this.waitEndsAtSignal()[kind];

    if (endsAt === undefined) {
      return null;
    }

    const remaining: number = Math.floor((endsAt - this.nowSignal()) / 1000);

    return remaining < 1 ? null : formatCountdown(remaining);
  }

  /** The local instant one waiting kind runs to, as the ISO text the date pipe turns into a clock time. */
  endsAtOf(kind: AuthWaitKind): string | null {
    const endsAt: number | undefined = this.waitEndsAtSignal()[kind];

    return endsAt === undefined || this.countdownOf(kind) === null ? null : new Date(endsAt).toISOString();
  }

  /* Ticking */
  private refresh(): void {
    this.nowSignal.set(Date.now());
    this.syncTicking();
  }

  /**
   * One second per wait, and none at all once both lapsed. The interval lives outside the zone so
   * the timer itself is not a macro task the app waits for; only the moment it advances the clock
   * is handed back, which is what lets the countdown repaint.
   */
  private syncTicking(): void {
    if (this.waiting() && this.timerId === undefined) {
      this.timerId = this.zone.runOutsideAngular((): number =>
        globalThis.setInterval((): void => {
          this.zone.run((): void => this.refresh());
        }, 1000),
      );
      return;
    }

    if (!this.waiting()) {
      this.stopTicking();
    }
  }

  private stopTicking(): void {
    if (this.timerId !== undefined) {
      globalThis.clearInterval(this.timerId);
      this.timerId = undefined;
    }
  }
}

/** `mm:ss` with a minute roll-over, because a bare second count reads as a much shorter wait. */
function formatCountdown(remainingSeconds: number): string {
  const minutes: number = Math.floor(remainingSeconds / 60);
  const seconds: number = remainingSeconds % 60;

  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}
