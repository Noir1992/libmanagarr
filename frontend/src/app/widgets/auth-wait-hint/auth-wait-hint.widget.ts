import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, Signal } from '@angular/core';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { AuthWaitKind, AuthWaitService } from '../../services/auth-wait.service';

/** One waiting kind as a line: how long is left, then the detail that puts it in context. */
interface AuthWaitLine {
  readonly retry: string;
  readonly detail: string;
}

/**
 * The shared wait indicator for every form the auth limits can refuse: sign-in, setup, the account
 * cards and the backup actions. The refusal itself keeps its own message where it appeared, so this
 * adds only what a single answer cannot know — how long the wait still runs, when it ends, and that
 * the ip limit does not care about the proof. It follows the session-wide waits, so the notice stays
 * while the user moves between those screens and disappears the moment the wait lapses.
 */
@Component({
  selector: 'app-auth-wait-hint',
  templateUrl: './auth-wait-hint.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-wait-hint.widget.scss',
})
export class AuthWaitHintWidget {
  /* Injections */
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly authWaitService: AuthWaitService = inject(AuthWaitService);

  /* Signals */
  protected readonly accountLockLine: Signal<AuthWaitLine | null> = computed((): AuthWaitLine | null => this.lineOf('account_lock'));
  protected readonly ipLimitLine: Signal<AuthWaitLine | null> = computed((): AuthWaitLine | null => this.lineOf('ip_limit'));

  /* Lines */
  /**
   * The remaining time ticks, so it stays out of the accessibility tree — a screen reader would
   * otherwise repeat the whole sentence every second. The detail line carries the same fact once, as
   * the instant the wait runs to, and is the part that stays readable.
   */
  private lineOf(kind: AuthWaitKind): AuthWaitLine | null {
    const countdown: string | null = this.authWaitService.countdownOf(kind);

    if (countdown === null) {
      return null;
    }

    return { retry: this.i18n.t('auth.retryIn', { countdown }), detail: this.detailOf(kind) };
  }

  private detailOf(kind: AuthWaitKind): string {
    if (kind === 'ip_limit') {
      return this.i18n.t('auth.ipLimitNote');
    }

    const endsAt: string | null = this.authWaitService.endsAtOf('account_lock');
    const time: string = endsAt === null ? '' : formatDate(endsAt, 'shortTime', this.appSettingsService.localeId());

    return this.i18n.t('auth.accountLockedUntil', { time });
  }
}
