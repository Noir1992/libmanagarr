import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  NgZone,
  OnDestroy,
  OnInit,
  Signal,
  signal,
  viewChild,
  WritableSignal,
} from '@angular/core';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatInput } from '@angular/material/input';
import { BackupApplyResult } from '../../../interfaces/backup/backup-apply-result';
import { BackupExportRequest } from '../../../interfaces/backup/backup-export-request';
import { BackupImportPreview } from '../../../interfaces/backup/backup-import-preview';
import { BackupProofRequest } from '../../../interfaces/backup/backup-proof-request';
import { BackupStatus } from '../../../interfaces/backup/backup-status';
import { AccountBackend } from '../../../interfaces/auth/account-backend';
import { FeedbackState } from '../../../interfaces/general/feedback-state';
import { AppDatePipe } from '../../../pipes/app-date.pipe';
import { AccountService } from '../../../services/account.service';
import { AuthRequestError } from '../../../services/auth-error.util';
import { AuthWaitService } from '../../../services/auth-wait.service';
import { BackupDownload, BackupService } from '../../../services/backup.service';
import { AuthWaitHintWidget } from '../../../widgets/auth-wait-hint/auth-wait-hint.widget';
import { FeedbackIconWidget } from '../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../widgets/info-icon/info-icon.widget';
import { SettingsUnsavedChangesBase } from '../settings-unsaved-changes.base';

/** What the tab knows about a restore: nothing, one is running, or the next start has to finish it. */
type RestoreState = 'idle' | 'busy' | 'restarting' | 'restartDelayed';

/** The three calls that spin their own button: only one of them ever runs at a time. */
type BackupAction = 'export' | 'import' | 'apply';

/** Last thing the status route reported about a finished or abandoned restore. */
interface RestoreNotice {
  readonly kind: 'restored' | 'rolledBack';
  readonly text: string;
}

type ExportFormGroup = FormGroup<{
  backupPassword: FormControl<string>;
  confirmBackupPassword: FormControl<string>;
  currentPassword: FormControl<string>;
  code: FormControl<string>;
}>;

type ImportFormGroup = FormGroup<{
  backupPassword: FormControl<string>;
}>;

type ApplyFormGroup = FormGroup<{
  currentPassword: FormControl<string>;
  code: FormControl<string>;
}>;

/**
 * Where a refused password has to be read back: the one that unlocks the archive or the one that
 * proves the account. The backend names the failure but never the field, so each card says which
 * of its own passwords a given code belongs to and keeps everything else as a card message.
 */
interface PasswordFieldErrors {
  readonly archivePassword: string | null;
  readonly accountPassword: string | null;
}

const NO_PASSWORD_ERRORS: PasswordFieldErrors = { archivePassword: null, accountPassword: null };

const EXPORT_PASSWORD_FIELDS: Readonly<Record<string, keyof PasswordFieldErrors>> = {
  backup_weak_password: 'archivePassword',
  backup_identity_verification_failed: 'accountPassword',
};

const IMPORT_PASSWORD_FIELDS: Readonly<Record<string, keyof PasswordFieldErrors>> = {
  backup_invalid_password: 'archivePassword',
};

const APPLY_PASSWORD_FIELDS: Readonly<Record<string, keyof PasswordFieldErrors>> = {
  backup_identity_verification_failed: 'accountPassword',
};

function passwordErrorsFor(field: keyof PasswordFieldErrors, message: string): PasswordFieldErrors {
  return field === 'archivePassword'
    ? { archivePassword: message, accountPassword: null }
    : { archivePassword: null, accountPassword: message };
}

/** The two halves of one button's mark, taken back together when its moment has passed. */
interface FeedbackSlot {
  readonly state: WritableSignal<FeedbackState>;
  readonly message: WritableSignal<string | null>;
}

/**
 * Backup tab: one archive out, one archive in, both asked for in place instead of a popup. An export
 * is streamed straight to the browser and a confirmed import is taken over by the next start only, so
 * the tab keeps its own small state machine instead of pretending either action finished on the spot.
 * Each card carries its own form: writing an archive wants a new password and the identity proof,
 * reading one wants the password inside it, restoring wants the proof alone. Passwords are handed to
 * the call that needs them and cleared on the way out, and the preview shows counts, versions and
 * dates because the archive holds API keys this screen must never display.
 */
@Component({
  selector: 'app-settings-backup-tab-widget',
  imports: [ReactiveFormsModule, MatButtonModule, MatInput, AuthWaitHintWidget, InfoIconWidget, FeedbackIconWidget, AppDatePipe],
  templateUrl: './settings-backup-tab.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-backup-tab.widget.scss',
})
export class SettingsBackupTabWidget extends SettingsUnsavedChangesBase implements OnInit, OnDestroy {
  /* Injections */
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);
  private readonly backupService: BackupService = inject(BackupService);
  private readonly accountService: AccountService = inject(AccountService);
  private readonly authWaitService: AuthWaitService = inject(AuthWaitService);
  private readonly zone: NgZone = inject(NgZone);
  private restartPollTimer?: ReturnType<typeof setTimeout>;
  private restartDeadline: number = 0;
  private restoreOperationId: string | null = null;
  private destroyed: boolean = false;
  /** One expiry per button, so a mark that nobody read is taken back while the next one is allowed to stay. */
  private readonly feedbackTimeouts: Partial<Record<BackupAction, ReturnType<typeof setTimeout>>> = {};

  /* View Queries */
  private readonly fileInput: () => ElementRef<HTMLInputElement> | undefined = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  /* I18n signals */
  protected readonly descriptionLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.description'));
  protected readonly exportTitleLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.exportTitle'));
  protected readonly exportInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.backup.export.info'));
  protected readonly exportLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.export'));
  protected readonly importTitleLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.importTitle'));
  protected readonly importInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.backup.import.info'));
  protected readonly chooseFileLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.chooseFile'));
  protected readonly validateLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.validate'));
  protected readonly backupPasswordLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.backupPassword'));
  protected readonly backupPasswordInfo: Signal<string> = computed((): string => this.i18n.t('settings.backup.backupPassword.info'));
  protected readonly confirmBackupPasswordLabel: Signal<string> = computed((): string =>
    this.i18n.t('settings.backup.confirmBackupPassword'),
  );
  protected readonly currentPasswordLabel: Signal<string> = computed((): string => this.i18n.t('settings.account.currentPassword'));
  protected readonly showPasswordLabel: Signal<string> = computed((): string => this.i18n.t('auth.showPassword'));
  protected readonly hidePasswordLabel: Signal<string> = computed((): string => this.i18n.t('auth.hidePassword'));
  protected readonly passwordLengthMessage: Signal<string> = computed((): string => this.i18n.t('auth.passwordLength'));
  protected readonly passwordMismatchMessage: Signal<string> = computed((): string => this.i18n.t('settings.backup.passwordMismatch'));
  protected readonly previewTitleLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.previewTitle'));
  protected readonly previewVersionLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.version'));
  protected readonly previewCreatedLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.created'));
  protected readonly previewExpiresLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.expires'));
  protected readonly previewLibrarySchemaLabel: Signal<string> = computed((): string =>
    this.i18n.t('settings.backup.preview.librarySchema'),
  );
  protected readonly previewInstancesLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.instances'));
  protected readonly previewSettingsLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.settings'));
  protected readonly previewContentLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.content'));
  protected readonly previewSizeLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.size'));
  protected readonly versionDiffersLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.preview.versionDiffers'));
  protected readonly applyLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.apply'));
  protected readonly discardLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.discard'));
  protected readonly waitingLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.waiting'));
  protected readonly restartDelayedLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.restartDelayed'));
  protected readonly operationRunningLabel: Signal<string> = computed((): string => this.i18n.t('settings.backup.operationRunning'));
  protected readonly retryLabel: Signal<string> = computed((): string => this.i18n.t('auth.errorRetry'));

  /* Signals */
  protected readonly busy: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly errorMessage: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly canRetry: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly twoFactorEnabled: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly selectedFile: WritableSignal<File | null> = signal<File | null>(null);
  protected readonly preview: WritableSignal<BackupImportPreview | null> = signal<BackupImportPreview | null>(null);
  protected readonly restoreState: WritableSignal<RestoreState> = signal<RestoreState>('idle');
  protected readonly restoreNotice: WritableSignal<RestoreNotice | null> = signal<RestoreNotice | null>(null);
  protected readonly exportFailure: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly importFailure: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly applyFailure: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly exportPasswordErrors: WritableSignal<PasswordFieldErrors> = signal<PasswordFieldErrors>(NO_PASSWORD_ERRORS);
  protected readonly importPasswordErrors: WritableSignal<PasswordFieldErrors> = signal<PasswordFieldErrors>(NO_PASSWORD_ERRORS);
  protected readonly applyPasswordErrors: WritableSignal<PasswordFieldErrors> = signal<PasswordFieldErrors>(NO_PASSWORD_ERRORS);
  protected readonly exportPasswordVisible: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly exportConfirmVisible: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly exportProofVisible: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly importPasswordVisible: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly applyProofVisible: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly exportUseRecovery: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly applyUseRecovery: WritableSignal<boolean> = signal<boolean>(false);

  /**
   * What each button says about its own call. A mark is worn for a moment and then taken back, as the
   * instance cards do, because a checkmark nobody saw would otherwise sit there until the tab is closed.
   */
  protected readonly exportFeedbackState: WritableSignal<FeedbackState> = signal<FeedbackState>(null);
  protected readonly exportFeedbackMessage: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly importFeedbackState: WritableSignal<FeedbackState> = signal<FeedbackState>(null);
  protected readonly importFeedbackMessage: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly applyFeedbackState: WritableSignal<FeedbackState> = signal<FeedbackState>(null);
  protected readonly applyFeedbackMessage: WritableSignal<string | null> = signal<string | null>(null);

  private readonly feedbackSlots: Record<BackupAction, FeedbackSlot> = {
    export: { state: this.exportFeedbackState, message: this.exportFeedbackMessage },
    import: { state: this.importFeedbackState, message: this.importFeedbackMessage },
    apply: { state: this.applyFeedbackState, message: this.applyFeedbackMessage },
  };

  /* Forms */
  protected readonly exportForm: ExportFormGroup = this.formBuilder.group({
    backupPassword: this.formBuilder.control<string>('', [Validators.required, Validators.minLength(12)]),
    confirmBackupPassword: this.formBuilder.control<string>('', [Validators.required]),
    currentPassword: this.formBuilder.control<string>('', [Validators.required]),
    code: this.formBuilder.control<string>(''),
  });

  protected readonly importForm: ImportFormGroup = this.formBuilder.group({
    backupPassword: this.formBuilder.control<string>('', [Validators.required]),
  });

  protected readonly applyForm: ApplyFormGroup = this.formBuilder.group({
    currentPassword: this.formBuilder.control<string>('', [Validators.required]),
    code: this.formBuilder.control<string>(''),
  });

  /* Derived state */
  protected readonly actionsBlocked: Signal<boolean> = computed(
    (): boolean => this.busy() || this.restoreState() === 'restarting' || this.restoreState() === 'restartDelayed',
  );

  // The three proof-bearing actions share the account behind them, so one known wait quietens all of
  // them. Choosing a file and dropping a checked archive stay allowed: neither asks anything the
  // limits protect, and a half-picked upload would otherwise be lost for nothing.
  protected readonly waiting: Signal<boolean> = this.authWaitService.waiting;
  protected readonly applySummary: Signal<string> = computed((): string => {
    const checked: BackupImportPreview | null = this.preview();

    return checked === null ? '' : this.i18n.t('settings.backup.applySummary', { version: checked.applicationVersion });
  });

  protected readonly exportCodeLabel: Signal<string> = computed((): string =>
    this.exportUseRecovery() ? this.i18n.t('auth.recoveryCode') : this.i18n.t('auth.code'),
  );

  protected readonly applyCodeLabel: Signal<string> = computed((): string =>
    this.applyUseRecovery() ? this.i18n.t('auth.recoveryCode') : this.i18n.t('auth.code'),
  );

  protected readonly exportRecoveryLabel: Signal<string> = computed((): string =>
    this.exportUseRecovery() ? this.i18n.t('auth.useAuthenticatorCode') : this.i18n.t('auth.useRecoveryCode'),
  );

  protected readonly applyRecoveryLabel: Signal<string> = computed((): string =>
    this.applyUseRecovery() ? this.i18n.t('auth.useAuthenticatorCode') : this.i18n.t('auth.useRecoveryCode'),
  );

  /** The new archive only leaves the installation once both spellings of its password agree. */
  protected get archivePasswordsMatch(): boolean {
    return this.exportForm.controls.backupPassword.value === this.exportForm.controls.confirmBackupPassword.value;
  }

  /** A field with a reason under it is framed as failing, whether this form or the backend found it. */
  protected get exportArchiveHasError(): boolean {
    const control: FormControl<string> = this.exportForm.controls.backupPassword;

    return (control.touched && control.hasError('minlength')) || this.exportPasswordErrors().archivePassword !== null;
  }

  protected get exportConfirmHasError(): boolean {
    return this.exportForm.controls.confirmBackupPassword.touched && !this.archivePasswordsMatch;
  }

  protected get canExport(): boolean {
    return this.exportForm.valid && this.archivePasswordsMatch;
  }

  protected get canImport(): boolean {
    return this.importForm.valid && this.selectedFile() !== null;
  }

  protected get canApply(): boolean {
    return this.applyForm.valid && this.preview() !== null;
  }

  /* Lifecycle */
  async ngOnInit(): Promise<void> {
    await Promise.all([this.loadAccountAsync(), this.loadStatusAsync()]);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopRestartPolling();
    for (const timeout of Object.values(this.feedbackTimeouts)) {
      if (timeout) {
        clearTimeout(timeout);
      }
    }
  }

  /* Unsaved-changes contract */
  protected override isBusy(): boolean {
    return this.actionsBlocked();
  }

  protected override hasUnsavedChanges(): boolean {
    return this.preview() !== null;
  }

  protected override buildUnsavedChangesMessage(): string {
    return this.i18n.t('settings.backup.discardQuestion');
  }

  protected override discardUnsavedChanges(): void {
    this.clearPreview();
  }

  /* Handlers */
  protected async exportAsync(): Promise<void> {
    if (this.actionsBlocked()) {
      return;
    }

    if (!this.canExport) {
      this.exportForm.markAllAsTouched();
      return;
    }

    const { backupPassword, currentPassword, code }: { backupPassword: string; currentPassword: string; code: string } =
      this.exportForm.getRawValue();

    await this.runInlineAsync('export', this.exportFailure, this.exportPasswordErrors, EXPORT_PASSWORD_FIELDS, async (): Promise<void> => {
      const request: BackupExportRequest = {
        backupPassword,
        ...this.proofOf(currentPassword, code, this.exportUseRecovery()),
      };
      const download: BackupDownload = await this.backupService.exportAsync(request);

      download.save();
      this.exportForm.reset({ backupPassword: '', confirmBackupPassword: '', currentPassword: '', code: '' });
      this.setActionFeedback('export', 'success', null);
    });
  }

  protected async validateAsync(): Promise<void> {
    const file: File | null = this.selectedFile();

    if (file === null || this.actionsBlocked()) {
      return;
    }

    if (!this.importForm.valid) {
      this.importForm.markAllAsTouched();
      return;
    }

    const backupPassword: string = this.importForm.controls.backupPassword.value;

    await this.runInlineAsync('import', this.importFailure, this.importPasswordErrors, IMPORT_PASSWORD_FIELDS, async (): Promise<void> => {
      const checked: BackupImportPreview = await this.backupService.validateImportAsync(file, backupPassword);

      this.importForm.reset();
      this.preview.set(checked);
      this.setActionFeedback('import', null, null);
    });
  }

  protected async applyAsync(): Promise<void> {
    const checked: BackupImportPreview | null = this.preview();

    if (checked === null || this.actionsBlocked()) {
      return;
    }

    if (!this.applyForm.valid) {
      this.applyForm.markAllAsTouched();
      return;
    }

    const { currentPassword, code }: { currentPassword: string; code: string } = this.applyForm.getRawValue();
    const proof: BackupProofRequest = this.proofOf(currentPassword, code, this.applyUseRecovery());

    await this.runInlineAsync('apply', this.applyFailure, this.applyPasswordErrors, APPLY_PASSWORD_FIELDS, async (): Promise<void> => {
      const applied: BackupApplyResult = await this.backupService.applyImportAsync(checked.importId, proof);

      this.applyForm.reset();
      this.clearPreview();
      this.restoreNotice.set(null);
      this.beginRestartMonitoring(applied.operationId);
      this.setActionFeedback('apply', null, null);
    });
  }

  /** Dropping a checked archive asks for no password, so its reason has no field to point at. */
  protected async discardAsync(): Promise<void> {
    const preview: BackupImportPreview | null = this.preview();
    if (preview === null || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set(null);
    this.clearPasswordErrors();

    try {
      await this.backupService.discardImportAsync(preview.importId);
      this.clearPreview();
    } catch (error: unknown) {
      this.errorMessage.set(this.backupService.translateError(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** A file input empties its own list once its value is reset, so the choice has to be read first. */
  protected handleFilePicked(input: HTMLInputElement): void {
    const file: File | null = input.files?.[0] ?? null;

    this.clearPreview();
    this.errorMessage.set(null);
    this.canRetry.set(false);
    this.importPasswordErrors.set(NO_PASSWORD_ERRORS);
    this.selectedFile.set(file);
  }

  protected toggleExportRecoveryUsage(): void {
    this.exportUseRecovery.update((current: boolean): boolean => !current);
    this.exportForm.controls.code.reset('');
  }

  protected toggleApplyRecoveryUsage(): void {
    this.applyUseRecovery.update((current: boolean): boolean => !current);
    this.applyForm.controls.code.reset('');
  }

  protected async retryAsync(): Promise<void> {
    this.errorMessage.set(null);
    this.canRetry.set(false);
    if (this.restoreState() === 'restartDelayed') {
      this.beginRestartMonitoring(this.restoreOperationId);
      return;
    }
    await Promise.all([this.loadAccountAsync(), this.loadStatusAsync()]);
  }

  protected instancesLabelOf(preview: BackupImportPreview): string {
    return this.i18n.tCount(
      preview.sonarrInstanceCount + preview.radarrInstanceCount,
      'settings.backup.preview.instanceOne',
      'settings.backup.preview.instanceMany',
      { sonarr: preview.sonarrInstanceCount.toString(), radarr: preview.radarrInstanceCount.toString() },
    );
  }

  protected sizeLabelOf(preview: BackupImportPreview): string {
    return this.i18n.t('settings.backup.preview.sizeValue', {
      archive: this.megabytesOf(preview.archiveBytes),
      expanded: this.megabytesOf(preview.uncompressedBytes),
    });
  }

  /* Helpers */
  private proofOf(currentPassword: string, code: string, useRecoveryCode: boolean): BackupProofRequest {
    const trimmedCode: string = code.trim();

    return {
      currentPassword,
      code: trimmedCode.length > 0 ? trimmedCode : undefined,
      useRecoveryCode,
    };
  }

  /**
   * One running action at a time, and its reason stays with the card that asked for it. A refused
   * password is the one failure with an input to fix, so it is handed to that input instead of the
   * card, exactly as the account area does. An archive the backend dropped for good leaves with the
   * preview, so its reason moves to the top of the tab where it is still readable after the card is gone.
   */
  private async runInlineAsync(
    running: BackupAction,
    failure: WritableSignal<string | null>,
    passwordErrors: WritableSignal<PasswordFieldErrors>,
    passwordFields: Readonly<Record<string, keyof PasswordFieldErrors>>,
    action: () => Promise<void>,
  ): Promise<void> {
    this.busy.set(true);
    this.setActionFeedback(running, 'loading', null);
    failure.set(null);
    passwordErrors.set(NO_PASSWORD_ERRORS);

    try {
      await action();
    } catch (error: unknown) {
      const message: string = this.backupService.translateError(error);
      const code: string | null = error instanceof AuthRequestError ? error.code : null;

      if (code === 'backup_import_expired') {
        this.clearPreview();
        this.errorMessage.set(message);
        this.setActionFeedback(running, null, null);
      } else {
        this.placeFailure(code, message, passwordFields, failure, passwordErrors);
        this.setActionFeedback(running, 'error', message);

        // A refusal that named a wait is worn until that wait lapses instead of for three seconds,
        // because the countdown under the buttons would otherwise be all that is left to explain why
        // retrying is pointless.
        if (this.authWaitService.waiting()) {
          this.holdActionFeedback(running);
        }
      }
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * What a button wears after its call, worn for a moment only. A mark nobody looked at would otherwise
   * stay on the card until the tab is closed, so state and message are taken back together - exactly the
   * way the instance cards handle theirs. A running button keeps its spinner until the answer arrives.
   */
  private setActionFeedback(action: BackupAction, feedback: FeedbackState, message: string | null): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.feedbackTimeouts[action];
    if (timeout) {
      clearTimeout(timeout);
      delete this.feedbackTimeouts[action];
    }

    const slot: FeedbackSlot = this.feedbackSlots[action];
    slot.state.set(feedback);
    slot.message.set(message);

    if (feedback === 'loading' || feedback === null) {
      return;
    }

    this.feedbackTimeouts[action] = setTimeout((): void => {
      slot.state.set(null);
      slot.message.set(null);
      delete this.feedbackTimeouts[action];
    }, 3000);
  }

  /**
   * Takes one button's expiry back, so its mark stays while the wait that produced it is still known.
   * The next call replaces the mark the same way the timed expiry would have.
   */
  private holdActionFeedback(action: BackupAction): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.feedbackTimeouts[action];

    if (timeout) {
      clearTimeout(timeout);
      delete this.feedbackTimeouts[action];
    }
  }

  /** A password the backend refused belongs to one input; anything else has no field to point at. */
  private placeFailure(
    code: string | null,
    message: string,
    passwordFields: Readonly<Record<string, keyof PasswordFieldErrors>>,
    failure: WritableSignal<string | null>,
    passwordErrors: WritableSignal<PasswordFieldErrors>,
  ): void {
    const field: keyof PasswordFieldErrors | undefined = code === null ? undefined : passwordFields[code];

    if (field === undefined) {
      failure.set(message);
    } else {
      passwordErrors.set(passwordErrorsFor(field, message));
    }
  }

  private clearPasswordErrors(): void {
    this.exportPasswordErrors.set(NO_PASSWORD_ERRORS);
    this.importPasswordErrors.set(NO_PASSWORD_ERRORS);
    this.applyPasswordErrors.set(NO_PASSWORD_ERRORS);
  }

  /** Drops the checked archive from the view and empties the picker, so the same file can be chosen again. */
  private clearPreview(): void {
    this.preview.set(null);
    this.selectedFile.set(null);

    const input: HTMLInputElement | undefined = this.fileInput()?.nativeElement;
    if (input !== undefined) {
      input.value = '';
    }
  }

  private megabytesOf(bytes: number): string {
    return (Math.round((bytes / (1024 * 1024)) * 10) / 10).toString();
  }

  /**
   * A failed page load is the one failure retrying repairs, so only this path offers the button. An
   * action the backend refused stays retryable by typing it again, where a retry would only repeat
   * the very mistake that was reported.
   */
  private reportLoadFailure(error: unknown): void {
    this.errorMessage.set(this.backupService.translateError(error));
    this.canRetry.set(true);
  }

  private async loadAccountAsync(): Promise<void> {
    try {
      const account: AccountBackend = await this.accountService.getAccountAsync();

      this.twoFactorEnabled.set(account.twoFactorEnabled);
    } catch (error: unknown) {
      this.reportLoadFailure(error);
    }
  }

  private beginRestartMonitoring(operationId: string | null): void {
    this.stopRestartPolling();
    this.restoreOperationId = operationId;
    this.restartDeadline = Date.now() + 120_000;
    this.restoreState.set('restarting');
    this.errorMessage.set(null);
    this.scheduleRestartPoll();
  }

  private stopRestartPolling(): void {
    if (this.restartPollTimer !== undefined) {
      clearTimeout(this.restartPollTimer);
      this.restartPollTimer = undefined;
    }
  }

  private scheduleRestartPoll(): void {
    if (this.destroyed) return;
    // Waiting for the server must not hold Angular's stability/rendering cycle open.
    this.zone.runOutsideAngular((): void => {
      this.restartPollTimer = setTimeout((): void => {
        void this.pollRestartAsync();
      }, 1000);
    });
  }

  private async pollRestartAsync(): Promise<void> {
    this.restartPollTimer = undefined;
    if (this.destroyed) return;
    try {
      const status: BackupStatus = await this.backupService.getStatusAsync();
      if (this.destroyed) return;
      const matches: boolean = this.restoreOperationId === null || status.operationId === this.restoreOperationId;
      if (matches && (status.phase === 'Restored' || status.phase === 'RestoreRolledBack')) {
        this.zone.run((): void => {
          this.restoreState.set('idle');
          this.restoreNotice.set(
            status.phase === 'Restored'
              ? { kind: 'restored', text: this.i18n.t('settings.backup.restored') }
              : { kind: 'rolledBack', text: this.i18n.t('settings.backup.restoreRolledBack') },
          );
          // The new page reads the same terminal status and displays it using the imported settings.
          this.backupService.reloadApplication();
        });
        return;
      }
    } catch {
      // Connection failures and temporary server errors are expected while the host is rebuilt.
      if (this.destroyed) return;
    }
    if (Date.now() >= this.restartDeadline) {
      this.zone.run((): void => {
        this.restoreState.set('restartDelayed');
      });
      return;
    }
    this.scheduleRestartPoll();
  }

  /** The status route is the only one that still answers while a restore waits for its restart. */
  private async loadStatusAsync(): Promise<void> {
    try {
      const status: BackupStatus = await this.backupService.getStatusAsync();

      switch (status.phase) {
        case 'Restored':
          this.restoreNotice.set({ kind: 'restored', text: this.i18n.t('settings.backup.restored') });
          this.restoreState.set('idle');
          break;
        case 'RestoreRolledBack':
          this.restoreNotice.set({ kind: 'rolledBack', text: this.i18n.t('settings.backup.restoreRolledBack') });
          this.restoreState.set('idle');
          break;
        case 'PreparingRestore':
        case 'RestartPending':
          this.beginRestartMonitoring(status.operationId);
          break;
        case 'Exporting':
        case 'Validating':
          this.restoreState.set('busy');
          break;
        default:
          this.restoreState.set('idle');
          break;
      }
    } catch (error: unknown) {
      this.reportLoadFailure(error);
    }
  }
}
