import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BackupExportRequest } from '../interfaces/backup/backup-export-request';
import { AppI18nService } from './app-i18n.service';
import { AuthRequestError } from './auth-error.util';
import { BackupDownload, BackupService } from './backup.service';

describe('BackupService', () => {
  let service: BackupService;
  let httpController: HttpTestingController;
  const i18nSpy: jasmine.SpyObj<AppI18nService> = jasmine.createSpyObj<AppI18nService>('AppI18nService', ['t']);

  i18nSpy.t.and.callFake((key: string): string => key);

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
