import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AccountBackend } from '../interfaces/auth/account-backend';
import { ChangePasswordRequest } from '../interfaces/auth/change-password-request';
import { RecoveryCodesBackend } from '../interfaces/auth/recovery-codes-backend';
import { TwoFactorProofRequest } from '../interfaces/auth/two-factor-proof-request';
import { TwoFactorSetupBackend } from '../interfaces/auth/two-factor-setup-backend';
import { AppI18nService } from './app-i18n.service';
import { toAuthRequestError } from './auth-error.util';
import { AuthWaitService } from './auth-wait.service';
import { AuthService } from './auth.service';

/**
 * Account management calls: profile, password change and the whole two-factor lifecycle.
 * Every failure surfaces as an `AuthRequestError` so the account tab can translate the
 * stable problem codes and validation field codes itself. Confirming or disabling two
 * factor authentication issues a brand-new session, which invalidates the cached
 * antiforgery token; the service drops it so the next mutating call mints a fresh one.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  /* Injections */
  private readonly http: HttpClient = inject(HttpClient);
  private readonly authService: AuthService = inject(AuthService);
  private readonly authWaitService: AuthWaitService = inject(AuthWaitService);
  private readonly i18n: AppI18nService = inject(AppI18nService);

  /* Queries */
  async getAccountAsync(): Promise<AccountBackend> {
    try {
      return await firstValueFrom(this.http.get<AccountBackend>('/api/account'));
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }

  /* Password */
  async changePasswordAsync(request: ChangePasswordRequest): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/account/password', request));
      this.authService.invalidateCsrfToken();
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }

  /* Two-factor lifecycle */
  async startTwoFactorSetupAsync(): Promise<TwoFactorSetupBackend> {
    try {
      return await firstValueFrom(this.http.post<TwoFactorSetupBackend>('/api/account/two-factor/setup', null));
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }

  async confirmTwoFactorSetupAsync(code: string): Promise<RecoveryCodesBackend> {
    try {
      const response: RecoveryCodesBackend = await firstValueFrom(
        this.http.post<RecoveryCodesBackend>('/api/account/two-factor/setup/confirm', { code }),
      );
      this.authService.invalidateCsrfToken();
      return response;
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }

  async cancelTwoFactorSetupAsync(): Promise<void> {
    try {
      await firstValueFrom(this.http.delete<void>('/api/account/two-factor/setup'));
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }

  async disableTwoFactorAsync(request: TwoFactorProofRequest): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/account/two-factor/disable', request));
      this.authService.invalidateCsrfToken();
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }

  async rotateRecoveryCodesAsync(request: TwoFactorProofRequest): Promise<RecoveryCodesBackend> {
    try {
      return await firstValueFrom(this.http.post<RecoveryCodesBackend>('/api/account/two-factor/recovery-codes', request));
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }
}
