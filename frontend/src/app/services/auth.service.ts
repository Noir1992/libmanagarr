import { HttpClient } from '@angular/common/http';
import { inject, Injectable, Signal, signal, WritableSignal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppLanguage } from '../interfaces/general/app-language';
import { AuthFlowState } from '../interfaces/general/auth-flow-state';
import { AuthStateBackend } from '../interfaces/auth/auth-state-backend';
import { LoginResponseBackend } from '../interfaces/auth/login-response-backend';
import { TwoFactorStateBackend } from '../interfaces/auth/two-factor-state-backend';
import { detectBrowserLanguage } from '../utils/browser-language-utils';
import { AppI18nService } from './app-i18n.service';
import { BACKEND_LANGUAGE_TO_APP_LANGUAGE } from './app-settings.service';
import { AuthRequestError, toAuthRequestError, translateAuthError } from './auth-error.util';
import { AuthWaitService } from './auth-wait.service';

/**
 * Client-side truth about the sign-in flow. The state comes exclusively from the backend
 * probe on `/api/auth/state`, never from readable cookies or local storage flags. The CSRF
 * bootstrap lives here as well: the antiforgery token is always minted for the CURRENT
 * identity, so the cached value is dropped on every identity change and lazily renewed by
 * the CSRF interceptor before the next mutating request.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  /* Injections */
  private readonly http: HttpClient = inject(HttpClient);
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly authWaitService: AuthWaitService = inject(AuthWaitService);

  /* Constants */
  private static readonly csrfCookieName: string = 'XSRF-TOKEN';
  private static readonly csrfUrl: string = '/api/auth/csrf';

  /* Signals */
  private readonly stateSignal: WritableSignal<AuthFlowState> = signal<AuthFlowState>('loading');
  private readonly errorLabelSignal: WritableSignal<string | null> = signal<string | null>(null);
  private readonly csrfTokenSignal: WritableSignal<string | null> = signal<string | null>(null);
  private readonly preferredLanguageSignal: WritableSignal<AppLanguage | null> = signal<AppLanguage | null>(null);
  readonly state: Signal<AuthFlowState> = this.stateSignal;
  readonly errorLabel: Signal<string | null> = this.errorLabelSignal;

  /** Configured language from the state probe; null while unknown, which covers first run and probe failures. */
  readonly preferredLanguage: Signal<AppLanguage | null> = this.preferredLanguageSignal;

  /* State */
  private initializationPromise?: Promise<void>;
  private sessionExpiredHandled: boolean = false;

  /* Lifecycle-adjacent flows */
  async initializeAsync(): Promise<void> {
    if (this.stateSignal() === 'loading' && this.initializationPromise !== undefined) {
      return this.initializationPromise;
    }

    this.stateSignal.set('loading');
    this.initializationPromise = this.probeStateAsync();

    try {
      await this.initializationPromise;
    } finally {
      this.initializationPromise = undefined;
    }
  }

  /* Mutating flows */
  async loginAsync(username: string, password: string, remember: boolean): Promise<boolean> {
    try {
      const response: LoginResponseBackend = await firstValueFrom(
        this.http.post<LoginResponseBackend>('/api/auth/login', { username, password, remember }),
      );

      if (!response.requiresTwoFactor) {
        this.onAuthenticated();
      }

      return response.requiresTwoFactor;
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.invalidCredentials')));
    }
  }

  async verifyTwoFactorAsync(code: string, useRecoveryCode: boolean, remember: boolean): Promise<void> {
    try {
      await firstValueFrom(this.http.post<LoginResponseBackend>('/api/auth/two-factor/verify', { code, useRecoveryCode, remember }));
      this.onAuthenticated();
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.invalidCode')));
    }
  }

  async setupAsync(username: string, password: string, confirmPassword: string): Promise<void> {
    try {
      await firstValueFrom(
        this.http.post<void>('/api/auth/setup', { username, password, confirmPassword, language: detectBrowserLanguage() }),
      );
      this.onAuthenticated();
    } catch (error) {
      throw this.authWaitService.record(toAuthRequestError(error, this.i18n.t('auth.loadFailed')));
    }
  }

  async logoutAsync(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
    this.onSignedOut();
  }

  /* Queries */
  async twoFactorStateAsync(): Promise<TwoFactorStateBackend> {
    try {
      return await firstValueFrom(this.http.get<TwoFactorStateBackend>('/api/auth/two-factor/state'));
    } catch (error) {
      throw toAuthRequestError(error, this.i18n.t('auth.loadFailed'));
    }
  }

  /* Session handling */
  handleSessionExpired(): boolean {
    if (this.sessionExpiredHandled) {
      return false;
    }

    this.sessionExpiredHandled = true;
    this.onSignedOut();
    return true;
  }

  /* CSRF bootstrap */
  async csrfTokenAsync(): Promise<string> {
    const cached: string | null = this.csrfTokenSignal();

    if (cached !== null) {
      return cached;
    }

    try {
      await firstValueFrom(this.http.get(AuthService.csrfUrl, { responseType: 'text' }));
    } catch {
      return '';
    }

    const token: string = AuthService.readCookie(AuthService.csrfCookieName);
    this.csrfTokenSignal.set(token);
    return token;
  }

  translate(error: AuthRequestError): string {
    return translateAuthError(error, this.i18n);
  }

  /** Drops the cached antiforgery token so the next mutating call mints one for the current identity. */
  invalidateCsrfToken(): void {
    this.csrfTokenSignal.set(null);
  }

  /* Helpers */
  private async probeStateAsync(): Promise<void> {
    try {
      const state: AuthStateBackend = await firstValueFrom(this.http.get<AuthStateBackend>('/api/auth/state'));
      this.applyProbeResult(state);
    } catch {
      this.csrfTokenSignal.set(null);
      this.preferredLanguageSignal.set(null);
      this.errorLabelSignal.set(this.i18n.t('auth.loadFailed'));
      this.stateSignal.set('error');
    }
  }

  private applyProbeResult(state: AuthStateBackend): void {
    this.csrfTokenSignal.set(null);
    this.errorLabelSignal.set(null);
    this.preferredLanguageSignal.set(
      state.language === undefined || state.language === null ? null : BACKEND_LANGUAGE_TO_APP_LANGUAGE[state.language],
    );

    if (state.authenticated) {
      this.stateSignal.set('authenticated');
      return;
    }

    this.stateSignal.set(state.setupRequired ? 'setupRequired' : 'anonymous');
  }

  private onAuthenticated(): void {
    this.csrfTokenSignal.set(null);
    this.sessionExpiredHandled = false;
    this.errorLabelSignal.set(null);
    this.authWaitService.clear();
    this.stateSignal.set('authenticated');
  }

  private onSignedOut(): void {
    this.csrfTokenSignal.set(null);
    this.stateSignal.set('anonymous');
  }

  private static readCookie(name: string): string {
    const cookies: string[] = globalThis.document.cookie.split(';');

    for (const cookie of cookies) {
      const separatorIndex: number = cookie.indexOf('=');

      if (separatorIndex < 0) {
        continue;
      }

      if (cookie.slice(0, separatorIndex).trim() === name) {
        return globalThis.decodeURIComponent(cookie.slice(separatorIndex + 1));
      }
    }

    return '';
  }
}
