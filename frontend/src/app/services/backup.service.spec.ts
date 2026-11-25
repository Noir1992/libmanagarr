import { signal } from '@angular/core';
import { SizeUnitSystem } from '../interfaces/general/size-unit-system';
import { AppSettingsService } from './app-settings.service';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import type { MockInstance } from 'vitest';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BackupExportRequest } from '../interfaces/backup/backup-export-request';
import { AppI18nService } from './app-i18n.service';
import { AuthRequestError } from './auth-error.util';
import { BackupDownload, BackupService } from './backup.service';

describe('BackupService', () => {
  let service: BackupService;
  let httpController: HttpTestingController;
  const i18nSpy: Record<'t', MockInstance> = {
    t: vi.fn<AppI18nService['t']>(),
  };

  i18nSpy.t.mockImplementation((key: string): string => key);

  const request: BackupExportRequest = {
    backupPassword: 'a-very-long-backup-passphrase',
    currentPassword: 'account-secret',
    code: undefined,
    useRecoveryCode: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: AppI18nService, useValue: i18nSpy }, provideHttpClient(withInterceptorsFromDi()), provideHttpClientTesting()],
    });

    service = TestBed.inject(BackupService);
    httpController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpController.verify();
  });

  /**
   * The export asks for a blob, so a failing call answers with the problem document inside one and
   * the service has to read that body before it can translate the failure.
   */
  async function failExportAsync(body: Blob, status: number): Promise<unknown> {
    const exportCall: Promise<BackupDownload> = service.exportAsync(request);

    httpController.expectOne('/api/backups/export').flush(body, { status, statusText: 'Bad Request' });

    return exportCall.catch((caught: unknown): unknown => caught);
  }

  function problemBody(fields: Record<string, unknown>): Blob {
    return new Blob([JSON.stringify({ status: 400, ...fields })]);
  }

  it('keeps the problem code of a failure that arrived inside the blob', async () => {
    const error: unknown = await failExportAsync(problemBody({ detail: 'Refused.', code: 'backup_invalid_password' }), 400);

    expect(error).toBeInstanceOf(AuthRequestError);
    expect((error as AuthRequestError).code).toBe('backup_invalid_password');
    expect(service.translateError(error)).toBe('settings.backup.errorInvalidPassword');
  });

  it('names a wrong account password instead of the generic failure', async () => {
    const error: unknown = await failExportAsync(
      problemBody({ title: 'The current password is incorrect.', code: 'backup_identity_verification_failed' }),
      400,
    );

    expect(service.translateError(error)).toBe('auth.currentPasswordIncorrect');
  });

  it('falls back to the generic failure when the body is no problem document', async () => {
    const error: unknown = await failExportAsync(new Blob(['<html>gateway refused</html>']), 502);

    expect(service.translateError(error)).toBe('settings.backup.error');
  });
});

describe('BackupService size errors', () => {
  let service: BackupService;
  let http: HttpTestingController;
  const units = signal<SizeUnitSystem>('Decimal');
  const i18n = new AppI18nService();

  beforeEach(() => {
    units.set('Decimal');
    i18n.setLanguage('de');
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AppI18nService, useValue: i18n },
        { provide: AppSettingsService, useValue: { sizeUnitSystem: units, localeId: () => 'de-DE' } },
      ],
    });
    service = TestBed.inject(BackupService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the selected units for upload errors and can retranslate the same bytes after switching', async () => {
    const pending = service.validateImportAsync(new File(['backup'], 'backup.lmbak'), 'password').catch((error: unknown) => error);
    http
      .expectOne('/api/backups/import/validate')
      .flush({ code: 'backup_size_limit_exceeded', title: 'Too large.', limitBytes: 536870912 }, { status: 413, statusText: 'Too Large' });
    const error = await pending;
    expect((error as AuthRequestError).limitBytes).toBe(536870912);
    expect(service.translateError(error)).toBe('Das Backup überschreitet die zulässige Größe. Grenze: 536,87 MB.');
    units.set('Binary');
    expect(service.translateError(error)).toBe('Das Backup überschreitet die zulässige Größe. Grenze: 512 MiB.');
  });

  it('preserves required bytes in blob errors and translates them using current settings', async () => {
    const pending = service
      .exportAsync({ backupPassword: 'password', currentPassword: 'password', useRecoveryCode: false })
      .catch((error: unknown) => error);
    http.expectOne('/api/backups/export').flush(
      new Blob([
        JSON.stringify({
          status: 507,
          code: 'backup_insufficient_disk_space',
          title: 'Not enough space.',
          requiredBytes: 3221225472,
        }),
      ]),
      { status: 507, statusText: 'Insufficient Storage' },
    );
    const error = await pending;
    expect((error as AuthRequestError).requiredBytes).toBe(3221225472);
    expect(service.translateError(error)).toBe('Es ist zu wenig Speicherplatz vorhanden. Benötigt: 3,22 GB.');
    units.set('Binary');
    expect(service.translateError(error)).toBe('Es ist zu wenig Speicherplatz vorhanden. Benötigt: 3 GiB.');
  });

  it('keeps older size failures without byte metadata usable', () => {
    expect(service.translateError(new AuthRequestError('Too large.', 413, 'backup_size_limit_exceeded', {}))).toBe(
      'Die Datei ist zu groß.',
    );
    expect(service.translateError(new AuthRequestError('No space.', 507, 'backup_insufficient_disk_space', {}))).toBe(
      'Es ist zu wenig Speicherplatz vorhanden.',
    );
  });
});
