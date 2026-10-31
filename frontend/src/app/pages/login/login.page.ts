import { ChangeDetectionStrategy, Component, computed, inject, OnInit, Signal, signal, WritableSignal } from '@angular/core';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatInput } from '@angular/material/input';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthFlowState } from '../../interfaces/general/auth-flow-state';
import { TwoFactorStateBackend } from '../../interfaces/auth/two-factor-state-backend';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { AuthRequestError, translateAuthError } from '../../services/auth-error.util';
import { AuthService } from '../../services/auth.service';
import { ForgotPasswordDialogComponent } from './forgot-password-dialog/forgot-password-dialog.component';

type LoginMode = 'credentials' | 'twoFactor';

type CredentialsFormGroup = FormGroup<{
  username: FormControl<string>;
  password: FormControl<string>;
  rememberMe: FormControl<boolean>;
}>;

type CodeFormGroup = FormGroup<{
  code: FormControl<string>;
}>;

const LOCAL_RETURN_URL_PATTERN: RegExp = /^\/(?!\/|\\)[^\s]*$/;

/**
 * Public sign-in page with its own minimal layout. It walks the credentials step, optionally
 * resumes a still-pending two-factor challenge after a reload, and offers the recovery-code
 * variant. All failures stay inside the form; nothing from this page touches local storage.
 */
@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckbox, MatInput],
  templateUrl: './login.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './login.page.scss',
})
export class LoginPage implements OnInit {
  /* Injections */
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly authService: AuthService = inject(AuthService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly router: Router = inject(Router);
  private readonly activatedRoute: ActivatedRoute = inject(ActivatedRoute);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);
  private readonly dialog: MatDialog = inject(MatDialog);

  /* I18n signals */
  protected readonly loginTitle: Signal<string> = computed((): string => this.i18n.t('auth.loginTitle'));
  protected readonly usernameLabel: Signal<string> = computed((): string => this.i18n.t('auth.username'));
  protected readonly passwordLabel: Signal<string> = computed((): string => this.i18n.t('auth.password'));
  protected readonly showPasswordLabel: Signal<string> = computed((): string => this.i18n.t('auth.showPassword'));
  protected readonly hidePasswordLabel: Signal<string> = computed((): string => this.i18n.t('auth.hidePassword'));
  protected readonly rememberMeLabel: Signal<string> = computed((): string => this.i18n.t('auth.rememberMe'));
  protected readonly signInLabel: Signal<string> = computed((): string => this.i18n.t('auth.signIn'));
  protected readonly forgotPasswordLabel: Signal<string> = computed((): string => this.i18n.t('auth.forgotPassword'));
  protected readonly twoFactorTitle: Signal<string> = computed((): string => this.i18n.t('auth.twoFactorTitle'));
  protected readonly twoFactorDescription: Signal<string> = computed((): string => this.i18n.t('auth.twoFactorDescription'));
  protected readonly recoveryDescription: Signal<string> = computed((): string => this.i18n.t('auth.recoveryDescription'));
  protected readonly codeLabel: Signal<string> = computed((): string => this.i18n.t('auth.code'));
  protected readonly recoveryCodeLabel: Signal<string> = computed((): string => this.i18n.t('auth.recoveryCode'));
  protected readonly verifyLabel: Signal<string> = computed((): string => this.i18n.t('auth.verify'));
  protected readonly useRecoveryLabel: Signal<string> = computed((): string => this.i18n.t('auth.useRecoveryCode'));
  protected readonly useAuthenticatorLabel: Signal<string> = computed((): string => this.i18n.t('auth.useAuthenticatorCode'));
  protected readonly backToLoginLabel: Signal<string> = computed((): string => this.i18n.t('auth.backToLogin'));
  protected readonly errorTitle: Signal<string> = computed((): string => this.i18n.t('auth.errorTitle'));
  protected readonly retryLabel: Signal<string> = computed((): string => this.i18n.t('auth.errorRetry'));
  protected readonly authState: Signal<AuthFlowState> = this.authService.state;
  protected readonly authErrorLabel: Signal<string | null> = this.authService.errorLabel;

  /* Signals */
  protected readonly passwordVisible: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly mode: WritableSignal<LoginMode> = signal<LoginMode>('credentials');
  protected readonly useRecoveryCode: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly busy: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly formError: WritableSignal<string | null> = signal<string | null>(null);
  // Keeps the chosen flag across the mode switch so the two-factor verify signs in the same way.
  private readonly pendingRemember: WritableSignal<boolean> = signal<boolean>(false);

  /* Forms */
  protected readonly credentialsForm: CredentialsFormGroup = this.formBuilder.group({
    username: this.formBuilder.control<string>('', [Validators.required]),
    password: this.formBuilder.control<string>('', [Validators.required]),
    rememberMe: this.formBuilder.control<boolean>(false),
  });
  protected readonly codeForm: CodeFormGroup = this.formBuilder.group({
    code: this.formBuilder.control<string>('', [Validators.required]),
  });

  /* Lifecycle */
  async ngOnInit(): Promise<void> {
    await this.resumePendingChallengeAsync();
  }

  /* Handlers */
  protected async submitCredentialsAsync(): Promise<void> {
    if (this.credentialsForm.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);

    try {
      const { username, password, rememberMe }: { username: string; password: string; rememberMe: boolean } =
        this.credentialsForm.getRawValue();
      this.pendingRemember.set(rememberMe);
      const requiresTwoFactor: boolean = await this.authService.loginAsync(username.trim(), password, rememberMe);

      if (requiresTwoFactor) {
        this.codeForm.controls.code.reset('');
        this.useRecoveryCode.set(false);
        this.mode.set('twoFactor');
        return;
      }

      await this.finishLoginAsync();
    } catch (error) {
      this.formError.set(this.messageFor(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async submitCodeAsync(): Promise<void> {
    if (this.codeForm.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);

    try {
      await this.authService.verifyTwoFactorAsync(
        this.codeForm.controls.code.getRawValue().trim(),
        this.useRecoveryCode(),
        this.pendingRemember(),
      );
      await this.finishLoginAsync();
    } catch (error) {
      if (error instanceof AuthRequestError && error.code === 'two_factor_challenge_invalid') {
        this.mode.set('credentials');
      }

      this.formError.set(this.messageFor(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected toggleRecoveryUsage(): void {
    this.useRecoveryCode.update((current: boolean): boolean => !current);
    this.formError.set(null);
    this.codeForm.controls.code.reset('');
  }

  protected backToCredentials(): void {
    this.mode.set('credentials');
    this.formError.set(null);
    this.codeForm.controls.code.reset('');
  }

  protected openForgotPasswordDialog(): void {
    this.dialog.open(ForgotPasswordDialogComponent);
  }

  protected async retryStateProbeAsync(): Promise<void> {
    await this.authService.initializeAsync();
    if (this.authService.state() === 'setupRequired') {
      await this.router.navigateByUrl('/setup', { replaceUrl: true });
    } else if (this.authService.state() === 'authenticated') {
      await this.finishLoginAsync();
    }
  }

  /* Helpers */
  private async resumePendingChallengeAsync(): Promise<void> {
    try {
      const state: TwoFactorStateBackend = await this.authService.twoFactorStateAsync();

      if (state.pending) {
        this.codeForm.controls.code.reset('');
        this.mode.set('twoFactor');
      }
    } catch {
      // A failed resume simply leaves the credentials step visible.
    }
  }

  private async finishLoginAsync(): Promise<void> {
    await this.appSettingsService.load();

    const returnUrl: string | null = this.activatedRoute.snapshot.queryParamMap.get('returnUrl');
    const target: string = returnUrl !== null && LOCAL_RETURN_URL_PATTERN.test(returnUrl) ? returnUrl : '/library';

    await this.router.navigateByUrl(target, { replaceUrl: true });
  }

  private messageFor(error: unknown): string {
    return error instanceof AuthRequestError ? translateAuthError(error, this.i18n) : this.i18n.t('auth.loadFailed');
  }
}
