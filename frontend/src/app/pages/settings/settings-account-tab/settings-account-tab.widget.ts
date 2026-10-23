import { ChangeDetectionStrategy, Component, computed, inject, OnInit, Signal, signal, WritableSignal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';
import { AccountBackend } from '../../../interfaces/auth/account-backend';
import { TwoFactorSetupBackend } from '../../../interfaces/auth/two-factor-setup-backend';
import { AccountService } from '../../../services/account.service';
import { AuthService } from '../../../services/auth.service';
import { AuthRequestError, translateAuthError, translateAuthFieldError } from '../../../services/auth-error.util';
import { InfoIconWidget } from '../../../widgets/info-icon/info-icon.widget';
import { SettingsUnsavedChangesBase } from '../settings-unsaved-changes.base';

type EnableStep = 'idle' | 'scan';

interface PasswordFieldErrors {
  readonly currentPassword: string | null;
  readonly newPassword: string | null;
  readonly confirmPassword: string | null;
}

type PasswordFormGroup = FormGroup<{
  currentPassword: FormControl<string>;
  newPassword: FormControl<string>;
  confirmPassword: FormControl<string>;
  code: FormControl<string>;
}>;

type ProofFormGroup = FormGroup<{
  currentPassword: FormControl<string>;
  code: FormControl<string>;
}>;

type EnableFormGroup = FormGroup<{
  code: FormControl<string>;
}>;

const NO_PASSWORD_ERRORS: PasswordFieldErrors = { currentPassword: null, newPassword: null, confirmPassword: null };
const RECOVERY_CODE_FILE_NAME: string = 'libmanagarr-recovery-codes.txt';

/**
 * Account tab: read-only profile, password change (forces a fresh login because every other
 * session is revoked) and the full two-factor lifecycle. Secrets (setup secret, issued
 * recovery codes) only live in memory and are dropped on discard, never persisted.
 */
@Component({
  selector: 'app-settings-account-tab-widget',
  imports: [ReactiveFormsModule, MatButtonModule, MatInput, InfoIconWidget],
  templateUrl: './settings-account-tab.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-account-tab.widget.scss',
})
export class SettingsAccountTabWidget extends SettingsUnsavedChangesBase implements OnInit {
  /* Injections */
  private readonly accountService: AccountService = inject(AccountService);
  private readonly authService: AuthService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);

  /* I18n signals */
  protected readonly descriptionLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.description'));
  protected readonly usernameLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.username'));
  protected readonly passwordTitleLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.passwordTitle'));
  protected readonly currentPasswordLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.currentPassword'));
  protected readonly newPasswordLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.newPassword'));
  protected readonly confirmPasswordLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.confirmPassword'));
  protected readonly passwordLengthMessage: Signal<string> = computed((): string => this.i18n.t('auth.passwordLength'));
  protected readonly passwordRequirements: Signal<string> = computed((): string => this.i18n.t('auth.passwordRequirements'));
  protected readonly changePasswordLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.changePassword'));
  protected readonly passwordChangedLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.passwordChanged'));
  protected readonly reloginLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.relogin'));
  protected readonly twoFactorTitleLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.twoFactorTitle'));
  protected readonly enableTwoFactorLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.enableTwoFactor'));
  protected readonly scanInstructionLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.scanInstruction'));
  protected readonly sharedKeyLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.sharedKeyLabel'));
  protected readonly confirmSetupLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.confirmSetup'));
  protected readonly cancelSetupLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.cancelSetup'));
  protected readonly recoveryCodesTitleLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.recoveryCodesTitle'));
  protected readonly recoveryCodesWarningLabel: Signal<string> = computed((): string =>
    this.i18n.t('settings.account.recoveryCodesWarning'),
  );
  protected readonly copyCodesLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.copyCodes'));
  protected readonly downloadCodesLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.downloadCodes'));
  protected readonly codesSavedLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.codesSaved'));
  protected readonly disableTwoFactorLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.disableTwoFactor'));
  protected readonly disableConfirmQuestionLabel: Signal<string> = computed((): string =>
    this.i18n.t('settings.account.disableConfirmQuestion'),
  );
  protected readonly rotateRecoveryCodesLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.rotateRecoveryCodes'));
  protected readonly codeLabel: Signal<string> = computed((): string => this.i18n.t('auth.code'));
  protected readonly recoveryCodeLabel: Signal<string> = computed((): string => this.i18n.t('auth.recoveryCode'));
  protected readonly useRecoveryLabel: Signal<string> = computed((): string => this.i18n.t('auth.useRecoveryCode'));
  protected readonly useAuthenticatorLabel: Signal<string> = computed((): string => this.i18n.t('auth.useAuthenticatorCode'));
  protected readonly retryLabel: Signal<string> = computed((): string => this.i18n.t('auth.errorRetry'));
  protected readonly loadingLabel: Signal<string> = computed((): string => this.i18n.t('common.loading'));

  /* Signals */
  protected readonly account: WritableSignal<AccountBackend | null> = signal<AccountBackend | null>(null);
  protected readonly accountLoading: WritableSignal<boolean> = signal<boolean>(true);
  protected readonly accountError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly twoFactorEnabled: Signal<boolean> = computed((): boolean => this.account()?.twoFactorEnabled ?? false);
  protected readonly recoveryCodesRemainingLabel: Signal<string> = computed((): string =>
    this.account() === null
      ? ''
      : this.i18n.tCount(
          this.account()?.recoveryCodesRemaining ?? 0,
          'settings.account.recoveryCodesRemainingOne',
          'settings.account.recoveryCodesRemaining',
        ),
  );

  protected readonly passwordBusy: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly passwordError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly passwordFieldErrors: WritableSignal<PasswordFieldErrors> = signal<PasswordFieldErrors>(NO_PASSWORD_ERRORS);
  protected readonly passwordChanged: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly passwordUseRecovery: WritableSignal<boolean> = signal<boolean>(false);

  protected readonly enableStep: WritableSignal<EnableStep> = signal<EnableStep>('idle');
  protected readonly enableBusy: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly enableError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly setup: WritableSignal<TwoFactorSetupBackend | null> = signal<TwoFactorSetupBackend | null>(null);
  protected readonly qrCodeUrl: Signal<string> = computed(
    (): string => `data:image/svg+xml;charset=utf-8,${encodeURIComponent(this.setup()?.qrCodeSvg ?? '')}`,
  );

  protected readonly issuedRecoveryCodes: WritableSignal<string[] | null> = signal<string[] | null>(null);

  protected readonly disableBusy: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly disableError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly disableUseRecovery: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly rotateBusy: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly rotateError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly rotateUseRecovery: WritableSignal<boolean> = signal<boolean>(false);

  /* Forms */
  protected readonly passwordForm: PasswordFormGroup = this.formBuilder.group(
    {
      currentPassword: this.formBuilder.control<string>('', [Validators.required]),
      newPassword: this.formBuilder.control<string>('', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]),
      confirmPassword: this.formBuilder.control<string>('', [Validators.required]),
      code: this.formBuilder.control<string>(''),
    },
    {
      validators: (form: AbstractControl): ValidationErrors | null =>
        form.get('newPassword')?.value === form.get('confirmPassword')?.value ? null : { passwordMismatch: true },
    },
  );
  protected readonly enableForm: EnableFormGroup = this.formBuilder.group({
    code: this.formBuilder.control<string>(''),
  });
  protected readonly disableForm: ProofFormGroup = this.formBuilder.group({
    currentPassword: this.formBuilder.control<string>('', [Validators.required]),
    code: this.formBuilder.control<string>(''),
  });
  protected readonly rotateForm: ProofFormGroup = this.formBuilder.group({
    currentPassword: this.formBuilder.control<string>('', [Validators.required]),
    code: this.formBuilder.control<string>(''),
  });

  /* Lifecycle */
  async ngOnInit(): Promise<void> {
    await this.loadAccountAsync();
  }

  /* Unsaved-changes contract */
  protected override isBusy(): boolean {
    return this.passwordBusy() || this.enableBusy() || this.disableBusy() || this.rotateBusy();
  }

  protected override hasUnsavedChanges(): boolean {
    return (
      this.issuedRecoveryCodes() !== null ||
      this.setup() !== null ||
      this.passwordForm.dirty ||
      this.enableForm.dirty ||
      this.disableForm.dirty ||
      this.rotateForm.dirty
    );
  }

  protected override buildUnsavedChangesMessage(): string {
    return this.issuedRecoveryCodes() !== null ? this.recoveryCodesWarningLabel() : super.buildUnsavedChangesMessage();
  }

  protected override discardUnsavedChanges(): void {
    this.passwordForm.reset({ currentPassword: '', newPassword: '', confirmPassword: '', code: '' });
    this.enableForm.reset({ code: '' });
    this.disableForm.reset({ currentPassword: '', code: '' });
    this.rotateForm.reset({ currentPassword: '', code: '' });
    this.setup.set(null);
    this.issuedRecoveryCodes.set(null);
    this.enableStep.set('idle');
    this.clearMessages();
  }

  /* Handlers */
  protected async changePasswordAsync(): Promise<void> {
    if (this.passwordForm.invalid || this.passwordBusy()) {
      return;
    }

    this.passwordBusy.set(true);
    this.passwordError.set(null);
    this.passwordFieldErrors.set(NO_PASSWORD_ERRORS);

    try {
      const {
        currentPassword,
        newPassword,
        confirmPassword,
        code,
      }: { currentPassword: string; newPassword: string; confirmPassword: string; code: string } = this.passwordForm.getRawValue();

      await this.accountService.changePasswordAsync({
        currentPassword,
        newPassword,
        confirmPassword,
        code: code.trim().length > 0 ? code.trim() : undefined,
        useRecoveryCode: this.passwordUseRecovery(),
      });

      this.passwordForm.reset({ currentPassword: '', newPassword: '', confirmPassword: '', code: '' });
      this.passwordUseRecovery.set(false);
      this.passwordChanged.set(true);
      this.authService.handleSessionExpired();
      await this.router.navigateByUrl('/login', { replaceUrl: true });
    } catch (error) {
      this.applyPasswordError(error);
    } finally {
      this.passwordBusy.set(false);
    }
  }

  protected async startReloginAsync(): Promise<void> {
    try {
      await this.authService.logoutAsync();
    } finally {
      globalThis.location.assign('/login');
    }
  }

  protected async startTwoFactorSetupAsync(): Promise<void> {
    if (this.enableBusy()) {
      return;
    }

    this.enableBusy.set(true);
    this.enableError.set(null);

    try {
      this.setup.set(await this.accountService.startTwoFactorSetupAsync());
      this.enableForm.reset({ code: '' });
      this.enableStep.set('scan');
    } catch (error) {
      this.enableError.set(this.translate(error));
    } finally {
      this.enableBusy.set(false);
    }
  }

  protected async confirmTwoFactorSetupAsync(): Promise<void> {
    if (this.enableForm.controls.code.invalid || this.enableBusy()) {
      return;
    }

    this.enableBusy.set(true);
    this.enableError.set(null);

    try {
      const response = await this.accountService.confirmTwoFactorSetupAsync(this.enableForm.controls.code.getRawValue().trim());

      this.setup.set(null);
      this.issuedRecoveryCodes.set(response.recoveryCodes);
      this.enableForm.reset({ code: '' });
      this.enableStep.set('idle');
      await this.loadAccountAsync();
    } catch (error) {
      this.enableError.set(this.translate(error));
    } finally {
      this.enableBusy.set(false);
    }
  }

  protected async cancelTwoFactorSetupAsync(): Promise<void> {
    this.enableBusy.set(true);
    this.enableError.set(null);

    try {
      await this.accountService.cancelTwoFactorSetupAsync();
    } catch (error) {
      this.enableError.set(this.translate(error));
    } finally {
      this.setup.set(null);
      this.enableForm.reset({ code: '' });
      this.enableStep.set('idle');
      this.enableBusy.set(false);
    }
  }

  protected async disableTwoFactorAsync(): Promise<void> {
    if (this.disableForm.invalid || this.disableBusy()) {
      return;
    }

    if (!globalThis.confirm(this.disableConfirmQuestionLabel())) {
      return;
    }

    this.disableBusy.set(true);
    this.disableError.set(null);

    try {
      await this.accountService.disableTwoFactorAsync(this.proofOf(this.disableForm));
      this.disableForm.reset({ currentPassword: '', code: '' });
      this.disableUseRecovery.set(false);
      await this.loadAccountAsync();
    } catch (error) {
      this.disableError.set(this.translate(error));
    } finally {
      this.disableBusy.set(false);
    }
  }

  protected async rotateRecoveryCodesAsync(): Promise<void> {
    if (this.rotateForm.invalid || this.rotateBusy()) {
      return;
    }

    this.rotateBusy.set(true);
    this.rotateError.set(null);

    try {
      const response = await this.accountService.rotateRecoveryCodesAsync(this.proofOf(this.rotateForm));

      this.rotateForm.reset({ currentPassword: '', code: '' });
      this.rotateUseRecovery.set(false);
      this.issuedRecoveryCodes.set(response.recoveryCodes);
      await this.loadAccountAsync();
    } catch (error) {
      this.rotateError.set(this.translate(error));
    } finally {
      this.rotateBusy.set(false);
    }
  }

  protected togglePasswordRecoveryUsage(): void {
    this.passwordUseRecovery.update((current: boolean): boolean => !current);
    this.passwordForm.controls.code.reset('');
  }

  protected toggleDisableRecoveryUsage(): void {
    this.disableUseRecovery.update((current: boolean): boolean => !current);
    this.disableForm.controls.code.reset('');
  }

  protected toggleRotateRecoveryUsage(): void {
    this.rotateUseRecovery.update((current: boolean): boolean => !current);
    this.rotateForm.controls.code.reset('');
  }

  protected async copyRecoveryCodesAsync(): Promise<void> {
    const codes: string[] | null = this.issuedRecoveryCodes();

    if (codes !== null) {
      await globalThis.navigator.clipboard.writeText(codes.join(globalThis.navigator.userAgent.includes('Windows') ? '\r\n' : '\n'));
    }
  }

  protected downloadRecoveryCodes(): void {
    const codes: string[] | null = this.issuedRecoveryCodes();

    if (codes === null) {
      return;
    }

    const blob: Blob = new Blob([codes.join('\n')], { type: 'text/plain' });
    const url: string = globalThis.URL.createObjectURL(blob);
    const anchor: HTMLAnchorElement = globalThis.document.createElement('a');

    anchor.href = url;
    anchor.download = RECOVERY_CODE_FILE_NAME;
    anchor.click();
    globalThis.URL.revokeObjectURL(url);
  }

  protected acknowledgeRecoveryCodes(): void {
    this.issuedRecoveryCodes.set(null);
  }

  protected async retryAccountLoadAsync(): Promise<void> {
    await this.loadAccountAsync();
  }

  /* Helpers */
  private async loadAccountAsync(): Promise<void> {
    this.accountLoading.set(true);
    this.accountError.set(null);

    try {
      this.account.set(await this.accountService.getAccountAsync());
    } catch (error) {
      this.accountError.set(this.translate(error));
    } finally {
      this.accountLoading.set(false);
    }
  }

  private proofOf(form: ProofFormGroup): { currentPassword: string; code?: string; useRecoveryCode?: boolean } {
    const { currentPassword, code }: { currentPassword: string; code: string } = form.getRawValue();
    const trimmedCode: string = code.trim();

    return {
      currentPassword,
      code: trimmedCode.length > 0 ? trimmedCode : undefined,
      useRecoveryCode: form === this.disableForm ? this.disableUseRecovery() : this.rotateUseRecovery(),
    };
  }

  private applyPasswordError(error: unknown): void {
    if (!(error instanceof AuthRequestError)) {
      this.passwordError.set(this.translate(error));
      return;
    }

    const fieldErrors: PasswordFieldErrors = {
      currentPassword: this.firstFieldMessage(error, 'currentPassword'),
      newPassword: this.firstFieldMessage(error, 'newPassword'),
      confirmPassword: this.firstFieldMessage(error, 'confirmPassword'),
    };

    this.passwordFieldErrors.set(fieldErrors);

    if (fieldErrors.currentPassword === null && fieldErrors.newPassword === null && fieldErrors.confirmPassword === null) {
      this.passwordError.set(translateAuthError(error, this.i18n));
    }
  }

  private firstFieldMessage(error: AuthRequestError, field: string): string | null {
    const codes: string[] | undefined = error.fieldErrors[field];
    const firstCode: string | undefined = codes?.[0];

    return firstCode === undefined ? null : translateAuthFieldError(firstCode, this.i18n);
  }

  private translate(error: unknown): string {
    return error instanceof AuthRequestError ? translateAuthError(error, this.i18n) : this.i18n.t('auth.loadFailed');
  }

  private clearMessages(): void {
    this.passwordError.set(null);
    this.passwordFieldErrors.set(NO_PASSWORD_ERRORS);
    this.enableError.set(null);
    this.disableError.set(null);
    this.rotateError.set(null);
    this.accountError.set(null);
  }
}
