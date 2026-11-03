import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BackupApplyResult } from '../../../interfaces/backup/backup-apply-result';
import { BackupExportRequest } from '../../../interfaces/backup/backup-export-request';
import { BackupImportPreview } from '../../../interfaces/backup/backup-import-preview';
import { BackupProofRequest } from '../../../interfaces/backup/backup-proof-request';
import { BackupStatus } from '../../../interfaces/backup/backup-status';
import { AccountBackend } from '../../../interfaces/auth/account-backend';
import { AppI18nService } from '../../../services/app-i18n.service';
import { AppSettingsService } from '../../../services/app-settings.service';
import { AccountService } from '../../../services/account.service';
import { AuthRequestError } from '../../../services/auth-error.util';
import { BackupDownload, BackupService } from '../../../services/backup.service';
import { SettingsBackupTabWidget } from './settings-backup-tab.widget';

describe('SettingsBackupTabWidget', () => {
  let fixture: ComponentFixture<SettingsBackupTabWidget>;
  let backup: jasmine.SpyObj<BackupService>;
  let account: jasmine.SpyObj<AccountService>;
  let download: jasmine.SpyObj<BackupDownload>;

  const longPassword: string = 'a-very-long-backup-passphrase';

  const checkedArchive: BackupImportPreview = {
    importId: '7f0f0f0f-0000-4000-8000-000000000001',
    expiresAt: '2026-11-03T12:00:00.000Z',
    applicationVersion: '1.4.0',
    createdAt: '2026-11-02T09:30:00.000Z',
    librarySchemaVersion: 7,
    sonarrInstanceCount: 2,
    radarrInstanceCount: 1,
    hasSeerrInstance: false,
    archiveBytes: 3 * 1024 * 1024,
    uncompressedBytes: 9 * 1024 * 1024,
    sameApplicationVersion: true,
  };

  const proof: BackupProofRequest = { currentPassword: 'account-secret', code: undefined, useRecoveryCode: false };

  function statusOf(phase: BackupStatus['phase'], operationId: string | null = null): BackupStatus {
    return { phase, code: null, changedAt: '2026-11-03T10:00:00.000Z', operationId };
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function valueOf(id: string): string {
    return element().querySelector<HTMLInputElement>(`#${id}`)?.value ?? '<missing>';
  }

  function setValue(id: string, value: string): void {
    const input: HTMLInputElement = element().querySelector<HTMLInputElement>(`#${id}`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function touch(id: string): void {
    const input: HTMLInputElement = element().querySelector<HTMLInputElement>(`#${id}`) as HTMLInputElement;
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  /** The eye button points at its field, so it is found by that and not by position. */
  function revealButton(id: string): HTMLButtonElement {
    return element().querySelector<HTMLButtonElement>(`button[aria-controls="${id}"]`) as HTMLButtonElement;
  }

  /** Fails with the label it looked for, so a renamed button is not mistaken for a broken flow. */
  function buttonOf(labelKey: string): HTMLButtonElement {
    const button: HTMLButtonElement | undefined = Array.from(element().querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(
      (candidate: HTMLButtonElement): boolean => candidate.textContent?.includes(labelKey) ?? false,
    );

    if (button === undefined) {
      fail(`no button labelled ${labelKey}`);
    }

    return button as HTMLButtonElement;
  }

  function hasButton(labelKey: string): boolean {
    return Array.from(element().querySelectorAll('button') as NodeListOf<HTMLButtonElement>).some(
      (candidate: HTMLButtonElement): boolean => candidate.textContent?.includes(labelKey) ?? false,
    );
  }

  function failureText(): string {
    return element().querySelector('.feedback.error')?.textContent ?? '<no failure shown>';
  }

  /** A refused password is shown under its own input, so that is where the reason has to be read. */
  function fieldErrorText(id: string): string {
    const field: Element | null = element().querySelector(`#${id}`)?.closest('.field') ?? null;

    return field?.querySelector('.field-error')?.textContent ?? '<no field error shown>';
  }

  /** The reason under a field is framed too, so both halves of the feedback have to agree on the field. */
  function hasErrorFrame(id: string): boolean {
    return element().querySelector(`#${id}`)?.classList.contains('has-error') ?? false;
  }

  /** Only the button whose own call is running may spin, so the spinner is read from that button. */
  function spinsOn(labelKey: string): boolean {
    return iconStateOf(labelKey) === 'loading';
  }

  /** What a button says about its own call right now: spinning, refused, delivered, or nothing yet. */
  function iconStateOf(labelKey: string): 'loading' | 'success' | 'error' | null {
    const icon: Element | null = buttonOf(labelKey).querySelector('mat-icon');

    if (icon?.classList.contains('spinning') ?? false) {
      return 'loading';
    }

    if (icon?.classList.contains('success') ?? false) {
      return 'success';
    }

    return (icon?.classList.contains('error') ?? false) ? 'error' : null;
  }

  /**
   * A clicked handler runs as its own async chain, so one stability wait is not enough to reach its
   * last statement. The timer tick drains what is still queued, the repaint makes the result visible.
   */
  async function settleAsync(): Promise<void> {
    await fixture.whenStable();
    await new Promise((resolve: (value: void) => void) => globalThis.setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  async function clickButton(labelKey: string): Promise<void> {
    buttonOf(labelKey).click();
    await settleAsync();
  }

  /** A mark is worn on a button for three seconds, so proving it went away means waiting that long. */
  async function waitForMarkExpiry(): Promise<void> {
    await new Promise((resolve: (value: void) => void) => globalThis.setTimeout(resolve, 3100));
    fixture.detectChanges();
  }

  async function fillExportFields(): Promise<void> {
    setValue('export-backup-password', longPassword);
    setValue('export-backup-password-confirm', longPassword);
    setValue('export-current-password', 'account-secret');
  }

  /**
   * A real file input drops its list the moment its value is reset, which is exactly what the widget
   * does so the same archive can be chosen again. A plain property would survive that reset and hide
   * the order in which the handler reads the choice.
   */
  function stubFilePicker(input: HTMLInputElement, file: File): void {
    let files: File[] = [file];

    Object.defineProperty(input, 'files', { configurable: true, get: (): File[] => files });
    Object.defineProperty(input, 'value', {
      configurable: true,
      get: (): string => (files.length > 0 ? `fakepath/${file.name}` : ''),
      set: (): void => {
        files = [];
      },
    });
  }

  async function pickArchive(file: File): Promise<void> {
    const input: HTMLInputElement = element().querySelector('.file-input') as HTMLInputElement;
    stubFilePicker(input, file);
    input.dispatchEvent(new Event('change'));
    await settleAsync();
  }

  async function create(options: { twoFactorEnabled?: boolean; accountFails?: boolean } = {}): Promise<void> {
    if (options.accountFails ?? false) {
      account.getAccountAsync.and.rejectWith(new Error('offline'));
    } else {
      account.getAccountAsync.and.resolveTo({
        username: 'admin',
        twoFactorEnabled: options.twoFactorEnabled ?? false,
        recoveryCodesRemaining: 8,
      } satisfies AccountBackend);
    }

    fixture = TestBed.createComponent(SettingsBackupTabWidget);
    fixture.detectChanges();
    await settleAsync();
  }

  /** A call that does not answer yet, so the running state can be observed before it ends. */
  function pendingCall<T>(): { readonly promise: Promise<T>; resolve: (value: T) => void } {
    let resolve: (value: T) => void = (): void => undefined;
    const promise: Promise<T> = new Promise<T>((resume: (value: T) => void): void => {
      resolve = resume;
    });

    return { promise, resolve };
  }

  beforeEach(async () => {
    download = jasmine.createSpyObj<BackupDownload>('BackupDownload', ['save']);
    backup = jasmine.createSpyObj<BackupService>('BackupService', [
      'exportAsync',
      'validateImportAsync',
      'applyImportAsync',
      'discardImportAsync',
      'getStatusAsync',
      'translateError',
      'reloadApplication',
    ]);
    backup.exportAsync.and.resolveTo(download);
    backup.validateImportAsync.and.resolveTo(checkedArchive);
    backup.applyImportAsync.and.resolveTo({
      operationId: 'op-1',
      appliedAt: '2026-11-03T10:00:00.000Z',
      restartRequired: true,
    } satisfies BackupApplyResult);
    backup.discardImportAsync.and.resolveTo();
    backup.getStatusAsync.and.resolveTo(statusOf('Idle'));
    backup.translateError.and.returnValue('translated failure');

    account = jasmine.createSpyObj<AccountService>('AccountService', ['getAccountAsync']);

    await TestBed.configureTestingModule({
      imports: [SettingsBackupTabWidget],
      providers: [
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string): string => key,
            tCount: (count: number, singularKey: string, pluralKey: string): string => `${count === 1 ? singularKey : pluralKey} ${count}`,
          },
        },
        { provide: AppSettingsService, useValue: { localeId: (): string => 'en-US' } },
        { provide: BackupService, useValue: backup },
        { provide: AccountService, useValue: account },
      ],
    }).compileComponents();
  });

  it('loads the account and the status behind its two cards', async () => {
    await create();

    expect(account.getAccountAsync).toHaveBeenCalledTimes(1);
    expect(backup.getStatusAsync).toHaveBeenCalledTimes(1);
    expect(element().textContent).toContain('settings.backup.exportTitle');
    expect(element().textContent).toContain('settings.backup.importTitle');
    expect(element().querySelector('.backup-notice')).toBeNull();
  });

  it('asks the export card for the archive password twice and the account password', async () => {
    await create();

    expect(element().querySelector('#export-backup-password')).toBeTruthy();
    expect(element().querySelector('#export-backup-password-confirm')).toBeTruthy();
    expect(element().querySelector('#export-current-password')).toBeTruthy();
    expect(element().querySelector('#export-code')).toBeNull();
    expect(buttonOf('settings.backup.export').disabled).toBeTrue();
  });

  it('hands the typed archive password and identity proof to the export and marks it delivered', async () => {
    await create();
    await fillExportFields();

    expect(buttonOf('settings.backup.export').disabled).toBeFalse();

    await clickButton('settings.backup.export');

    expect(backup.exportAsync).toHaveBeenCalledOnceWith({
      backupPassword: longPassword,
      currentPassword: 'account-secret',
      code: undefined,
      useRecoveryCode: false,
    } satisfies BackupExportRequest);
    expect(download.save).toHaveBeenCalledTimes(1);
    expect(iconStateOf('settings.backup.export')).toBe('success');
  });

  /** The delivered mark says nothing after the moment it was earned, so it is worn briefly, not forever. */
  it('takes the delivered mark off the export button once its moment has passed', async () => {
    await create();
    await fillExportFields();

    await clickButton('settings.backup.export');

    expect(iconStateOf('settings.backup.export')).toBe('success');

    await waitForMarkExpiry();

    expect(iconStateOf('settings.backup.export')).toBeNull();
  });

  it('clears both archive passwords once the archive left for the browser', async () => {
    await create();
    await fillExportFields();

    await clickButton('settings.backup.export');

    expect(valueOf('export-backup-password')).toBe('');
    expect(valueOf('export-backup-password-confirm')).toBe('');
    expect(valueOf('export-current-password')).toBe('');
  });

  it('holds a refused account proof at its own input and lets the attempt be made again', async () => {
    await create();
    backup.exportAsync.and.rejectWith(new AuthRequestError('refused', 400, 'backup_identity_verification_failed', {}));
    await fillExportFields();

    await clickButton('settings.backup.export');

    expect(fieldErrorText('export-current-password')).toContain('translated failure');
    expect(hasErrorFrame('export-current-password')).toBeTrue();
    expect(hasErrorFrame('export-backup-password')).toBeFalse();
    expect(iconStateOf('settings.backup.export')).toBe('error');
    expect(element().querySelector('.feedback.error')).toBeNull();
    expect(buttonOf('settings.backup.export').disabled).toBeFalse();

    await clickButton('settings.backup.export');

    expect(backup.exportAsync).toHaveBeenCalledTimes(2);
  });

  /** The button stops pointing at a refusal once its moment has passed; the field keeps reasoning about it. */
  it('takes the refusal mark off the button while the reason under the field stays readable', async () => {
    await create();
    backup.exportAsync.and.rejectWith(new AuthRequestError('refused', 400, 'backup_identity_verification_failed', {}));
    await fillExportFields();

    await clickButton('settings.backup.export');

    expect(iconStateOf('settings.backup.export')).toBe('error');

    await waitForMarkExpiry();

    expect(iconStateOf('settings.backup.export')).toBeNull();
    expect(fieldErrorText('export-current-password')).toContain('translated failure');
  });

  it('holds a too weak archive password at the field that asked for it', async () => {
    await create();
    backup.exportAsync.and.rejectWith(new AuthRequestError('weak', 400, 'backup_weak_password', {}));
    await fillExportFields();

    await clickButton('settings.backup.export');

    expect(fieldErrorText('export-backup-password')).toContain('translated failure');
    expect(fieldErrorText('export-current-password')).toBe('<no field error shown>');
    expect(hasErrorFrame('export-backup-password')).toBeTrue();
    expect(hasErrorFrame('export-current-password')).toBeFalse();
  });

  it('offers no retry button for an action the backend refused, because retrying repeats the mistake', async () => {
    await create();
    backup.exportAsync.and.rejectWith(new AuthRequestError('refused', 400, 'backup_identity_verification_failed', {}));
    await fillExportFields();

    await clickButton('settings.backup.export');

    expect(hasButton('auth.errorRetry')).toBeFalse();
  });

  it('locks the export card while its call runs and lets it go again afterwards', async () => {
    await create();
    const pending: { promise: Promise<BackupDownload>; resolve: (download: BackupDownload) => void } = pendingCall<BackupDownload>();
    backup.exportAsync.and.returnValue(pending.promise);
    await fillExportFields();

    buttonOf('settings.backup.export').click();
    fixture.detectChanges();

    expect(buttonOf('settings.backup.export').disabled).toBeTrue();
    expect(spinsOn('settings.backup.export')).toBeTrue();
    expect(backup.exportAsync).toHaveBeenCalledTimes(1);

    pending.resolve(download);
    await settleAsync();

    expect(spinsOn('settings.backup.export')).toBeFalse();

    // A finished export empties its fields on purpose, so the released lock shows itself on the next attempt.
    await fillExportFields();

    expect(buttonOf('settings.backup.export').disabled).toBeFalse();

    await clickButton('settings.backup.export');

    expect(backup.exportAsync).toHaveBeenCalledTimes(2);
  });

  it('drops the previous reason the moment a new attempt starts', async () => {
    await create();
    backup.exportAsync.and.rejectWith(new AuthRequestError('refused', 400, 'backup_invalid_password', {}));
    await fillExportFields();
    await clickButton('settings.backup.export');

    expect(element().querySelector('.feedback.error')).toBeTruthy();

    const pending: { promise: Promise<BackupDownload>; resolve: (download: BackupDownload) => void } = pendingCall<BackupDownload>();
    backup.exportAsync.and.returnValue(pending.promise);

    buttonOf('settings.backup.export').click();
    fixture.detectChanges();

    expect(element().querySelector('.feedback.error')).toBeNull();

    pending.resolve(download);
    await settleAsync();
  });

  it('refuses an export whose archive passwords differ and names the reason once touched', async () => {
    await create();

    setValue('export-backup-password', longPassword);
    setValue('export-backup-password-confirm', 'a-different-passphrase');
    setValue('export-current-password', 'account-secret');
    touch('export-backup-password-confirm');

    expect(element().querySelector('.field-error')?.textContent).toContain('settings.backup.passwordMismatch');
    expect(hasErrorFrame('export-backup-password-confirm')).toBeTrue();
    expect(buttonOf('settings.backup.export').disabled).toBeTrue();
    expect(backup.exportAsync).not.toHaveBeenCalled();
  });

  it('reports a too short archive password without calling the backend', async () => {
    await create();

    setValue('export-backup-password', 'short');
    touch('export-backup-password');
    setValue('export-backup-password-confirm', 'short');
    setValue('export-current-password', 'account-secret');

    expect(element().querySelector('.field-error')?.textContent).toContain('auth.passwordLength');
    expect(backup.exportAsync).not.toHaveBeenCalled();
  });

  it('shows the code field for a two factor account and swaps it for a recovery code', async () => {
    await create({ twoFactorEnabled: true });

    expect(element().querySelector('#export-code')).toBeTruthy();

    setValue('export-backup-password', longPassword);
    setValue('export-backup-password-confirm', longPassword);
    setValue('export-current-password', 'account-secret');
    setValue('export-code', ' 4 3 2 1 ');

    await clickButton('auth.useRecoveryCode');

    expect(buttonOf('auth.useAuthenticatorCode')).toBeTruthy();
    expect(valueOf('export-code')).toBe('');

    setValue('export-code', 'recovery-code');

    await clickButton('settings.backup.export');

    expect(backup.exportAsync).toHaveBeenCalledOnceWith({
      backupPassword: longPassword,
      currentPassword: 'account-secret',
      code: 'recovery-code',
      useRecoveryCode: true,
    } satisfies BackupExportRequest);
  });

  it('reveals and hides every password the tab asks for', async () => {
    await create({ twoFactorEnabled: true });

    for (const id of ['export-backup-password', 'export-backup-password-confirm', 'export-current-password']) {
      const input: HTMLInputElement = element().querySelector<HTMLInputElement>(`#${id}`) as HTMLInputElement;
      const toggle: HTMLButtonElement = revealButton(id);

      expect(input.type).toBe('password');

      toggle.click();
      fixture.detectChanges();

      expect(input.type).toBe('text');
      expect(toggle.getAttribute('aria-label')).toBe('auth.hidePassword');

      toggle.click();
      fixture.detectChanges();

      expect(input.type).toBe('password');
      expect(toggle.getAttribute('aria-label')).toBe('auth.showPassword');
    }
  });

  it('offers nothing to import before a file was picked', async () => {
    await create();

    expect(element().querySelector('#import-backup-password')).toBeNull();
    expect(hasButton('settings.backup.validate')).toBeFalse();
  });

  it('asks the import card for the password inside the archive and shows what is inside', async () => {
    await create();
    const file: File = new File(['archive'], 'libmanagarr-2026-11-02.lmbak');
    await pickArchive(file);

    expect(element().textContent).toContain('libmanagarr-2026-11-02.lmbak');
    expect(buttonOf('settings.backup.validate').disabled).toBeTrue();

    setValue('import-backup-password', 'archive-passphrase');

    expect(buttonOf('settings.backup.validate').disabled).toBeFalse();

    await clickButton('settings.backup.validate');

    expect(backup.validateImportAsync).toHaveBeenCalledOnceWith(file, 'archive-passphrase');
    expect(valueOf('backup-preview-version')).toBe('1.4.0');
    expect(element().querySelector('#backup-preview-two-factor')).toBeNull();
    expect(element().querySelector('#backup-preview-auth-schema')).toBeNull();
    expect(valueOf('backup-preview-instances')).toContain('settings.backup.preview.instanceMany');
    expect(valueOf('backup-preview-instances')).toContain('3');
    expect(valueOf('backup-preview-content')).toBe('settings.backup.preview.settings');
    expect(element().querySelector('.backup-warning')).toBeNull();
  });

  it('keeps a corrupt archive in the import card with its password still typed', async () => {
    await create();
    await pickArchive(new File(['archive'], 'libmanagarr.lmbak'));
    backup.validateImportAsync.and.rejectWith(new AuthRequestError('corrupt', 400, 'backup_corrupt_archive', {}));

    setValue('import-backup-password', 'archive-passphrase');

    await clickButton('settings.backup.validate');

    expect(failureText()).toContain('translated failure');
    expect(valueOf('import-backup-password')).toBe('archive-passphrase');
    expect(element().querySelector('#backup-preview-version')).toBeNull();
  });

  it('holds a wrong archive password at the field that was asked for it', async () => {
    await create();
    await pickArchive(new File(['archive'], 'libmanagarr.lmbak'));
    backup.validateImportAsync.and.rejectWith(new AuthRequestError('wrong', 400, 'backup_invalid_password', {}));

    setValue('import-backup-password', 'archive-passphrase');

    await clickButton('settings.backup.validate');

    expect(fieldErrorText('import-backup-password')).toContain('translated failure');
    expect(hasErrorFrame('import-backup-password')).toBeTrue();
    expect(element().querySelector('.feedback.error')).toBeNull();
    expect(valueOf('import-backup-password')).toBe('archive-passphrase');
  });

  it('warns when the checked archive comes from another application version', async () => {
    await create();
    fixture.componentInstance['preview'].set({ ...checkedArchive, sameApplicationVersion: false });
    fixture.detectChanges();

    expect(element().querySelector('.backup-warning')?.textContent).toContain('settings.backup.preview.versionDiffers');
  });

  it('asks the preview card for the identity proof alone and then waits for the restart', async () => {
    await create();
    fixture.componentInstance['preview'].set(checkedArchive);
    fixture.componentInstance['selectedFile'].set(new File(['archive'], 'libmanagarr.lmbak'));
    fixture.detectChanges();

    expect(element().querySelector('#apply-current-password')).toBeTruthy();
    expect(element().querySelector('.backup-summary')?.textContent).toContain('settings.backup.applySummary');
    expect(buttonOf('settings.backup.apply').disabled).toBeTrue();

    setValue('apply-current-password', 'account-secret');

    expect(buttonOf('settings.backup.apply').disabled).toBeFalse();

    await clickButton('settings.backup.apply');

    expect(backup.applyImportAsync).toHaveBeenCalledOnceWith(checkedArchive.importId, proof);
    expect(element().querySelector('#backup-preview-version')).toBeNull();
    expect(element().querySelector('.backup-notice')?.textContent).toContain('settings.backup.waiting');
  });

  it('holds a refused proof at the preview card input and keeps the archive checked', async () => {
    await create();
    fixture.componentInstance['preview'].set(checkedArchive);
    fixture.detectChanges();
    backup.applyImportAsync.and.rejectWith(new AuthRequestError('refused', 400, 'backup_identity_verification_failed', {}));

    setValue('apply-current-password', 'wrong-secret');

    await clickButton('settings.backup.apply');

    expect(fieldErrorText('apply-current-password')).toContain('translated failure');
    expect(hasErrorFrame('apply-current-password')).toBeTrue();
    expect(element().querySelector('.feedback.error')).toBeNull();
    expect(element().querySelector('#backup-preview-version')).toBeTruthy();
  });

  it('drops the checked archive the moment the backend let it go and names the reason', async () => {
    await create();
    fixture.componentInstance['preview'].set(checkedArchive);
    fixture.detectChanges();
    backup.applyImportAsync.and.rejectWith(new AuthRequestError('gone', 404, 'backup_import_expired', {}));

    setValue('apply-current-password', 'account-secret');

    await clickButton('settings.backup.apply');

    expect(element().textContent).toContain('translated failure');
    expect(fixture.componentInstance['preview']()).toBeNull();
    expect(element().querySelector('#backup-preview-version')).toBeNull();
  });

  it('discards a checked archive and frees the picker again', async () => {
    await create();
    await pickArchive(new File(['archive'], 'libmanagarr.lmbak'));
    fixture.componentInstance['preview'].set(checkedArchive);
    fixture.detectChanges();

    await clickButton('settings.backup.discard');

    expect(backup.discardImportAsync).toHaveBeenCalledOnceWith(checkedArchive.importId);
    expect(element().querySelector('#backup-preview-version')).toBeNull();
    expect((element().querySelector('.file-input') as HTMLInputElement).value).toBe('');
  });

  it('shows automatic restart progress while a restore waits for the next host', async () => {
    backup.getStatusAsync.and.resolveTo(statusOf('RestartPending'));
    await create();

    expect(element().querySelector('.backup-notice')?.textContent).toContain('settings.backup.waiting');
  });

  /** Drives one scheduled poll without waiting for real network/restart delays. */
  async function pollOnce(): Promise<void> {
    const widget = fixture.componentInstance;
    widget['stopRestartPolling']();
    await widget['pollRestartAsync']();
    fixture.detectChanges();
  }

  it('waits through downtime, ignores unrelated completions, and reloads only after its own restore', async () => {
    await create();
    fixture.componentInstance['preview'].set(checkedArchive);
    fixture.detectChanges();
    setValue('apply-current-password', 'account-secret');
    await clickButton('settings.backup.apply');

    expect(element().textContent).toContain('settings.backup.waiting');
    expect(element().textContent).not.toContain('settings.backup.restartDelayed');
    expect(buttonOf('settings.backup.export').disabled).toBeTrue();
    expect(fixture.componentInstance.canDeactivate()).toBeFalse();

    backup.getStatusAsync.and.rejectWith(new Error('offline during restart'));
    await pollOnce();
    expect(backup.reloadApplication).not.toHaveBeenCalled();
    expect(hasButton('auth.errorRetry')).toBeFalse();

    backup.getStatusAsync.and.resolveTo(statusOf('Restored', 'an-older-operation'));
    await pollOnce();
    expect(backup.reloadApplication).not.toHaveBeenCalled();
    expect(element().textContent).toContain('settings.backup.waiting');

    backup.getStatusAsync.and.resolveTo(statusOf('Restored', 'op-1'));
    await pollOnce();
    expect(backup.reloadApplication).toHaveBeenCalledTimes(1);
    expect(element().textContent).toContain('settings.backup.restored');
    expect(fixture.componentInstance['restartPollTimer']).toBeUndefined();
    expect(fixture.componentInstance.canDeactivate()).toBeTrue();
  });

  it('reports a rollback and refreshes the application after the matching restart', async () => {
    await create();
    fixture.componentInstance['beginRestartMonitoring']('op-1');
    backup.getStatusAsync.and.resolveTo(statusOf('RestoreRolledBack', 'op-1'));

    await pollOnce();

    expect(element().textContent).toContain('settings.backup.restoreRolledBack');
    expect(element().querySelector('.backup-warning')).not.toBeNull();
    expect(backup.reloadApplication).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance['restartPollTimer']).toBeUndefined();
  });

  it('offers a connection retry after a timeout without repeating the restore command', async () => {
    await create();
    fixture.componentInstance['beginRestartMonitoring']('op-1');
    fixture.componentInstance['restartDeadline'] = Date.now() - 1;
    backup.getStatusAsync.and.rejectWith(new Error('still offline'));

    await pollOnce();

    expect(element().textContent).toContain('settings.backup.restartDelayed');
    expect(hasButton('auth.errorRetry')).toBeTrue();
    expect(buttonOf('settings.backup.export').disabled).toBeTrue();
    expect(backup.applyImportAsync).not.toHaveBeenCalled();

    await clickButton('auth.errorRetry');
    expect(element().textContent).toContain('settings.backup.waiting');
    backup.getStatusAsync.and.resolveTo(statusOf('Restored', 'op-1'));
    await pollOnce();
    expect(backup.reloadApplication).toHaveBeenCalledTimes(1);
    expect(backup.applyImportAsync).not.toHaveBeenCalled();
  });

  it('cancels scheduled and in-flight restart monitoring when the tab is destroyed', async () => {
    await create();
    fixture.componentInstance['beginRestartMonitoring']('op-1');
    fixture.componentInstance['stopRestartPolling']();
    const pending = pendingCall<BackupStatus>();
    backup.getStatusAsync.and.returnValue(pending.promise);
    const polling: Promise<void> = fixture.componentInstance['pollRestartAsync']();

    fixture.destroy();
    pending.resolve(statusOf('Restored', 'op-1'));
    await polling;

    expect(backup.reloadApplication).not.toHaveBeenCalled();
    expect(fixture.componentInstance['restartPollTimer']).toBeUndefined();
  });

  it('reports a finished restore without asking to rotate recovery codes', async () => {
    backup.getStatusAsync.and.resolveTo(statusOf('Restored'));
    await create();

    const notice: HTMLElement = element().querySelector('.backup-notice') as HTMLElement;
    expect(notice.textContent).toContain('settings.backup.restored');
    expect(notice.classList.contains('backup-warning')).toBeFalse();
    expect(element().querySelector('.backup-hint')).toBeNull();
  });

  it('marks a rolled back restore as a failure', async () => {
    backup.getStatusAsync.and.resolveTo(statusOf('RestoreRolledBack'));
    await create();

    const notice: HTMLElement = element().querySelector('.backup-notice') as HTMLElement;
    expect(notice.textContent).toContain('settings.backup.restoreRolledBack');
    expect(notice.classList.contains('backup-warning')).toBeTrue();
  });

  it('offers retry for a failed load and reloads account and status when used', async () => {
    await create({ accountFails: true });

    expect(element().textContent).toContain('translated failure');
    expect(hasButton('auth.errorRetry')).toBeTrue();

    account.getAccountAsync.and.resolveTo({
      username: 'admin',
      twoFactorEnabled: false,
      recoveryCodesRemaining: 8,
    } satisfies AccountBackend);
    await clickButton('auth.errorRetry');

    expect(account.getAccountAsync).toHaveBeenCalledTimes(2);
    expect(backup.getStatusAsync).toHaveBeenCalledTimes(2);
    expect(element().textContent).not.toContain('translated failure');
  });

  it('asks the page to keep the tab until the checked archive is dropped', async () => {
    await create();
    spyOn(globalThis, 'confirm').and.returnValue(false);

    expect(fixture.componentInstance.canDeactivate()).toBeTrue();

    fixture.componentInstance['preview'].set(checkedArchive);

    expect(fixture.componentInstance.canDeactivate()).toBeFalse();
    expect(backup.discardImportAsync).not.toHaveBeenCalled();
  });
});
