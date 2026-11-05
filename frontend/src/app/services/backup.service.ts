import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';
import { BackupApplyResult } from '../interfaces/backup/backup-apply-result';
import { BackupExportRequest } from '../interfaces/backup/backup-export-request';
import { BackupImportPreview } from '../interfaces/backup/backup-import-preview';
import { BackupProofRequest } from '../interfaces/backup/backup-proof-request';
import { BackupStatus } from '../interfaces/backup/backup-status';
import { AppI18nService } from './app-i18n.service';
import { AuthRequestError, toAuthRequestError, translateAuthError } from './auth-error.util';
import { AuthWaitService } from './auth-wait.service';
import { TranslationKey } from './i18n/_i18n-key';
import { AuthService } from './auth.service';

/**
 * Export and import of settings and library data. Secrets only ever live in the request body:
 * the archive is streamed straight into the browser download and an uploaded file is handed over
 * as form data, so neither a backup password nor decrypted content is cached anywhere. A confirmed
 * import ends the application, which is why the restart state is read from the status route
 * instead of being assumed.
 */
@Injectable({ providedIn: 'root' })
export class BackupService {
  /* Injections */
  private readonly http: HttpClient = inject(HttpClient);
  private readonly authService: AuthService = inject(AuthService);
  private readonly authWaitService: AuthWaitService = inject(AuthWaitService);
  private readonly i18n: AppI18nService = inject(AppI18nService);

  /* Commands */
  async exportAsync(request: BackupExportRequest): Promise<BackupDownload> {
    try {
      const response: HttpResponse<Blob> = await firstValueFrom(
        this.http.post('/api/backups/export', request, { responseType: 'blob', observe: 'response' }),
      );

      return new BackupDownload(response.body ?? new Blob(), this.fileNameFrom(response.headers.get('content-disposition')));
    } catch (error) {
      throw await this.toErrorAsync(error);
    }
  }

  async validateImportAsync(file: File, backupPassword: string): Promise<BackupImportPreview> {
    const form: FormData = new FormData();
    form.append('file', file, file.name);
    form.append('backupPassword', backupPassword);

    try {
      return await firstValueFrom(this.http.post<BackupImportPreview>('/api/backups/import/validate', form));
    } catch (error) {
      throw await this.toErrorAsync(error);
    }
  }

  async applyImportAsync(importId: string, request: BackupProofRequest): Promise<BackupApplyResult> {
    try {
      return await firstValueFrom(this.http.post<BackupApplyResult>(`/api/backups/import/${encodeURIComponent(importId)}/apply`, request));
    } catch (error) {
      throw await this.toErrorAsync(error);
    }
  }

  async discardImportAsync(importId: string): Promise<void> {
    try {
      await firstValueFrom(this.http.delete<void>(`/api/backups/import/${encodeURIComponent(importId)}`));
    } catch (error) {
      throw await this.toErrorAsync(error);
    }
  }

  /* Queries */
  async getStatusAsync(): Promise<BackupStatus> {
    try {
      return await firstValueFrom(this.http.get<BackupStatus>('/api/backups/status').pipe(timeout(5000)));
    } catch (error) {
      throw await this.toErrorAsync(error);
    }
  }

  /** A fresh SPA drops all cached settings and domain state after the backend restore. */
  reloadApplication(): void {
    globalThis.location.reload();
  }

  /** Translates the stable backup problem codes; every other failure keeps its server wording. */
  translateError(error: unknown): string {
    if (!(error instanceof AuthRequestError)) {
      return error instanceof Error && error.message.trim().length > 0 ? error.message : this.i18n.t('settings.backup.error');
    }

    const key: TranslationKey | undefined = error.code === null ? undefined : BACKUP_ERROR_KEYS[error.code];

    return key === undefined ? translateAuthError(error, this.i18n) : this.i18n.t(key);
  }

  /* Helpers */
  private fileNameFrom(contentDisposition: string | null): string {
    const match: RegExpMatchArray | null = contentDisposition?.match(/filename="?([^";]+)"?/) ?? null;

    return match?.[1] ?? 'libmanagarr-backup.lmbak';
  }

  /**
   * A failed export answers with a problem document inside the blob, so the body has to be read
   * before the failure can be translated; the decoded document alone reaches the stable problem
   * code, and the answer header has to be handed over with it because the response object is gone
   * by then. Both are what tells a locked account from a busy ip address, so the wait is recorded
   * here as well. The antiforgery token is dropped for the same reason the account area does it: a
   * rejected mutating call may already have consumed it.
   */
  private async toErrorAsync(error: unknown): Promise<AuthRequestError> {
    const decoded: unknown =
      error instanceof HttpErrorResponse && error.error instanceof Blob ? await readBlobAsProblem(error.error) : error;
    const retryAfter: string | null = error instanceof HttpErrorResponse ? error.headers.get('Retry-After') : null;
    const requestError: AuthRequestError = this.authWaitService.record(
      toAuthRequestError(decoded, this.i18n.t('settings.backup.error'), retryAfter),
    );

    this.authService.invalidateCsrfToken();

    return requestError;
  }
}

/** Archive bytes handed to the browser; the password never reaches this object. */
export class BackupDownload {
  constructor(
    readonly blob: Blob,
    readonly fileName: string,
  ) {}

  save(): void {
    const url: string = globalThis.URL.createObjectURL(this.blob);
    const anchor: HTMLAnchorElement = globalThis.document.createElement('a');

    anchor.href = url;
    anchor.download = this.fileName;
    anchor.click();
    globalThis.URL.revokeObjectURL(url);
  }
}

const BACKUP_ERROR_KEYS: Readonly<Record<string, TranslationKey>> = {
  backup_invalid_password: 'settings.backup.errorInvalidPassword',
  backup_corrupt_archive: 'settings.backup.errorCorrupt',
  backup_unsupported_format: 'settings.backup.errorUnsupportedFormat',
  backup_unsupported_schema: 'settings.backup.errorUnsupportedSchema',
  backup_unsafe_path: 'settings.backup.errorUnsafePath',
  backup_size_limit_exceeded: 'settings.backup.errorTooLarge',
  backup_missing_component: 'settings.backup.errorMissingComponent',
  backup_checksum_mismatch: 'settings.backup.errorChecksum',
  backup_integrity_check_failed: 'settings.backup.errorIntegrity',
  backup_insufficient_disk_space: 'settings.backup.errorDiskSpace',
  backup_operation_in_progress: 'settings.backup.errorInProgress',
  backup_writers_not_paused: 'settings.backup.errorPaused',
  backup_import_expired: 'settings.backup.errorExpired',
  backup_import_unknown: 'settings.backup.errorUnknown',
  backup_import_conflict: 'settings.backup.errorConflict',
  backup_identity_verification_failed: 'auth.currentPasswordIncorrect',
  backup_restore_pending: 'settings.backup.errorRestorePending',
  backup_weak_password: 'settings.backup.errorWeakPassword',
};

/** The problem document inside a failed blob body, or nothing when the body is not one. */
async function readBlobAsProblem(blob: Blob): Promise<unknown> {
  try {
    const parsed: unknown = JSON.parse(await blob.text()) as unknown;

    return typeof parsed === 'object' && parsed !== null ? parsed : null;
  } catch {
    return null;
  }
}
