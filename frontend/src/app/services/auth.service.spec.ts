import { HttpClient, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TwoFactorStateBackend } from '../interfaces/auth/two-factor-state-backend';
import { AppI18nService } from './app-i18n.service';
import { AuthRequestError } from './auth-error.util';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpController: HttpTestingController;
  let http: HttpClient;
  const i18nSpy: jasmine.SpyObj<AppI18nService> = jasmine.createSpyObj<AppI18nService>('AppI18nService', ['t']);

  i18nSpy.t.and.callFake((key: string): string => key);

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: AppI18nService, useValue: i18nSpy }, provideHttpClient(withInterceptorsFromDi()), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    httpController = TestBed.inject(HttpTestingController);
    http = TestBed.inject(HttpClient);
  });

  afterEach(() => {
    httpController.verify();
    globalThis.document.cookie = 'XSRF-TOKEN=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT';
  });

  async function probeState(setupRequired: boolean, authenticated: boolean): Promise<void> {
    const probe: Promise<void> = service.initializeAsync();
    const request = httpController.expectOne('/api/auth/state');

    request.flush({ setupRequired, authenticated });
    await probe;
  }

  it('should report the setup requirement from the state probe', async () => {
    await probeState(true, false);

    expect(service.state()).toBe('setupRequired');
  });

  it('should report an anonymous session when no setup is required', async () => {
    await probeState(false, false);

    expect(service.state()).toBe('anonymous');
    expect(service.errorLabel()).toBeNull();
  });

  it('should switch to the error state when the probe fails', async () => {
    const probe: Promise<void> = service.initializeAsync();
    const request = httpController.expectOne('/api/auth/state');

    request.error(new ErrorEvent('network error'));
    await probe;

    expect(service.state()).toBe('error');
    expect(service.errorLabel()).toBe('auth.loadFailed');
    expect(service.preferredLanguage()).toBeNull();
  });

  it('should expose the configured backend language as the preferred language', async () => {
    const probe: Promise<void> = service.initializeAsync();
    const request = httpController.expectOne('/api/auth/state');

    request.flush({ setupRequired: false, authenticated: false, language: 'De' });
    await probe;

    expect(service.preferredLanguage()).toBe('de');
  });

  it('should keep the browser fallback when the state carries no language', async () => {
    await probeState(true, false);

    expect(service.preferredLanguage()).toBeNull();
  });

  it('should authenticate directly when the login needs no second factor', async () => {
    const login: Promise<boolean> = service.loginAsync('admin', 'secret123456', true);
    const request = httpController.expectOne('/api/auth/login');

    request.flush({ requiresTwoFactor: false });

    expect(await login).toBeFalse();
    expect(service.state()).toBe('authenticated');
    expect(request.request.body).toEqual({ username: 'admin', password: 'secret123456', remember: true });
  });

  it('should report a pending second factor without authenticating', async () => {
    const login: Promise<boolean> = service.loginAsync('admin', 'secret123456', false);

    httpController.expectOne('/api/auth/login').flush({ requiresTwoFactor: true });

    expect(await login).toBeTrue();
    expect(service.state()).not.toBe('authenticated');
  });

  it('should surface the stable problem code of a rejected login', async () => {
    const login: Promise<boolean> = service.loginAsync('admin', 'wrong-password', false);

    httpController
      .expectOne('/api/auth/login')
      .flush({ detail: 'Nope.', code: 'invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });

    const error: unknown = await login.catch((caught: unknown): unknown => caught);

    expect(error).toBeInstanceOf(AuthRequestError);
    expect((error as AuthRequestError).code).toBe('invalid_credentials');
    expect((error as AuthRequestError).status).toBe(401);
  });

  it('should authenticate after a verified two-factor code', async () => {
    const verify: Promise<void> = service.verifyTwoFactorAsync('654321', false, true);
    const request = httpController.expectOne('/api/auth/two-factor/verify');

    request.flush({ requiresTwoFactor: false });
    await verify;

    expect(request.request.body).toEqual({ code: '654321', useRecoveryCode: false, remember: true });
    expect(service.state()).toBe('authenticated');
  });

  it('should preserve its state when the logout call fails', async () => {
    const logout: Promise<void> = service.logoutAsync();

    httpController.expectOne('/api/auth/logout').flush({ detail: 'Broken.' }, { status: 500, statusText: 'Server Error' });

    await logout.catch((): void => undefined);

    expect(service.state()).toBe('loading');
  });

  it('should handle a session expiry exactly once', async () => {
    await probeState(false, false);

    expect(service.handleSessionExpired()).toBeTrue();
    expect(service.state()).toBe('anonymous');
    expect(service.handleSessionExpired()).toBeFalse();
  });

  it('should cache the antiforgery token until it is invalidated', async () => {
    globalThis.document.cookie = 'XSRF-TOKEN=t1; path=/';

    const first: Promise<string> = service.csrfTokenAsync();
    httpController.expectOne('/api/auth/csrf').flush('');
    expect(await first).toBe('t1');
    expect(await service.csrfTokenAsync()).toBe('t1');

    service.invalidateCsrfToken();
    globalThis.document.cookie = 'XSRF-TOKEN=t2; path=/';

    const second: Promise<string> = service.csrfTokenAsync();
    httpController.expectOne('/api/auth/csrf').flush('');
    expect(await second).toBe('t2');
  });

  it('should expose the two-factor state of a pending challenge', async () => {
    const query: Promise<TwoFactorStateBackend> = service.twoFactorStateAsync();

    httpController.expectOne('/api/auth/two-factor/state').flush({ pending: true, expiresAt: '2026-10-19T12:00:00Z' });

    await expectAsync(query).toBeResolvedTo({ pending: true, expiresAt: '2026-10-19T12:00:00Z' });
  });

  it('should clear the cached antiforgery token when the session ends', async () => {
    globalThis.document.cookie = 'XSRF-TOKEN=t1; path=/';

    const first: Promise<string> = service.csrfTokenAsync();
    httpController.expectOne('/api/auth/csrf').flush('');
    expect(await first).toBe('t1');

    service.handleSessionExpired();
    globalThis.document.cookie = 'XSRF-TOKEN=t3; path=/';

    const second: Promise<string> = service.csrfTokenAsync();
    httpController.expectOne('/api/auth/csrf').flush('');
    expect(await second).toBe('t3');
  });

  it('should keep the http client available for the api calls', () => {
    expect(http).toBeDefined();
  });
  it('keeps the authenticated state when logout fails', async () => {
    await probeState(false, true);
    const logout = service.logoutAsync();
    const outcome = logout.catch((error: unknown) => error);
    httpController.expectOne('/api/auth/logout').flush({}, { status: 503, statusText: 'Unavailable' });
    await outcome;
    expect(service.state()).toBe('authenticated');
  });
  for (const [browserLanguage, language] of [
    ['de-DE', 'de'],
    ['fr-CA', 'fr'],
    ['es-ES', 'en'],
  ]) {
    it(`includes ${language} in setup for browser language ${browserLanguage}`, async () => {
      spyOnProperty(globalThis.navigator, 'language', 'get').and.returnValue(browserLanguage);
      const setup = service.setupAsync('admin', 'test-password-value', 'test-password-value');
      const request = httpController.expectOne('/api/auth/setup');
      expect(request.request.body).toEqual({
        username: 'admin',
        password: 'test-password-value',
        confirmPassword: 'test-password-value',
        language,
      });
      request.flush(null);
      await setup;
      expect(service.state()).toBe('authenticated');
    });
  }
});
