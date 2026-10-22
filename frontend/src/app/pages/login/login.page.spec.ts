import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { AuthService } from '../../services/auth.service';
import { ForgotPasswordDialogComponent } from './forgot-password-dialog/forgot-password-dialog.component';
import { LoginPage } from './login.page';

@Component({ selector: 'app-library-noop', template: '' })
class LibraryNoopPage {}

describe('LoginPage', () => {
  let httpController: HttpTestingController;
  let authService: AuthService;
  let settingsLoadSpy: jasmine.Spy;

  const i18nSpy: jasmine.SpyObj<AppI18nService> = jasmine.createSpyObj<AppI18nService>('AppI18nService', ['t']);

  i18nSpy.t.and.callFake((key: string): string => key);

  beforeEach(() => {
    settingsLoadSpy = jasmine.createSpy('load').and.resolveTo(undefined);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'library', component: LibraryNoopPage },
          { path: 'setup', component: LibraryNoopPage },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AppI18nService, useValue: i18nSpy },
        { provide: AppSettingsService, useValue: { load: settingsLoadSpy } },
      ],
    });

    httpController = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
  });

  afterEach(() => {
    httpController.verify();
  });

  async function settle(): Promise<void> {
    await new Promise<void>((resolve: () => void): void => {
      setTimeout(resolve);
    });
  }

  async function createPage(pendingChallenge: boolean): Promise<ComponentFixture<LoginPage>> {
    const fixture = TestBed.createComponent(LoginPage);

    fixture.detectChanges();
    httpController
      .expectOne('/api/auth/two-factor/state')
      .flush({ pending: pendingChallenge, expiresAt: pendingChallenge ? '2026-10-19T12:00:00Z' : null });
    await settle();
    fixture.detectChanges();

    return fixture;
  }

  function setValue(fixture: ComponentFixture<LoginPage>, selector: string, value: string): void {
    const input: HTMLInputElement = fixture.nativeElement.querySelector(selector);

    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('should show the credentials form when no challenge is pending', async () => {
    const fixture = await createPage(false);

    expect(fixture.nativeElement.querySelector('#login-username')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#login-code')).toBeNull();
  });

  it('should resume a still pending two-factor challenge', async () => {
    const fixture = await createPage(true);

    expect(fixture.nativeElement.querySelector('#login-code')).not.toBeNull();
  });

  it('should sign in and load the settings when no second factor is required', async () => {
    const fixture = await createPage(false);

    setValue(fixture, '#login-username', 'admin');
    setValue(fixture, '#login-password', 'secret123456');
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    httpController.expectOne('/api/auth/login').flush({ requiresTwoFactor: false });
    await settle();

    expect(authService.state()).toBe('authenticated');
    expect(settingsLoadSpy).toHaveBeenCalled();
  });

  it('sends the remember-me flag with the login request when checked', async () => {
    const fixture = await createPage(false);

    setValue(fixture, '#login-username', 'admin');
    setValue(fixture, '#login-password', 'secret123456');
    (fixture.nativeElement.querySelector('mat-checkbox input') as HTMLInputElement).click();
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    const loginRequest = httpController.expectOne('/api/auth/login');
    expect(loginRequest.request.body).toEqual({ username: 'admin', password: 'secret123456', remember: true });

    loginRequest.flush({ requiresTwoFactor: false });
    await settle();

    expect(authService.state()).toBe('authenticated');
  });

  it('keeps the checked remember-me flag for the two-factor verify request', async () => {
    const fixture = await createPage(false);

    setValue(fixture, '#login-username', 'admin');
    setValue(fixture, '#login-password', 'secret123456');
    (fixture.nativeElement.querySelector('mat-checkbox input') as HTMLInputElement).click();
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    httpController.expectOne('/api/auth/login').flush({ requiresTwoFactor: true });
    await settle();
    fixture.detectChanges();

    setValue(fixture, '#login-code', '654321');
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    const verifyRequest = httpController.expectOne('/api/auth/two-factor/verify');
    expect(verifyRequest.request.body).toEqual({ code: '654321', useRecoveryCode: false, remember: true });

    verifyRequest.flush({ requiresTwoFactor: false });
    await settle();

    expect(authService.state()).toBe('authenticated');
  });

  it('should keep a rejected login inside the form with a translated message', async () => {
    const fixture = await createPage(false);

    setValue(fixture, '#login-username', 'admin');
    setValue(fixture, '#login-password', 'wrong-password');
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    httpController
      .expectOne('/api/auth/login')
      .flush({ detail: 'Nope.', code: 'invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });
    await settle();
    fixture.detectChanges();

    const errorText: string | null = fixture.nativeElement.querySelector('.field-error')?.textContent ?? null;

    expect(errorText).toContain('auth.invalidCredentials');
    expect(authService.state()).not.toBe('authenticated');
  });

  it('opens the password recovery dialog from the credentials form', async () => {
    const dialogOpenSpy: jasmine.Spy = spyOn(TestBed.inject(MatDialog), 'open');
    const fixture = await createPage(false);

    (fixture.nativeElement.querySelector('.forgot-password-button') as HTMLButtonElement).click();

    expect(dialogOpenSpy).toHaveBeenCalledWith(ForgotPasswordDialogComponent);
  });

  it('should walk the two-factor step and authenticate after the code verifies', async () => {
    const fixture = await createPage(false);

    setValue(fixture, '#login-username', 'admin');
    setValue(fixture, '#login-password', 'secret123456');
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    httpController.expectOne('/api/auth/login').flush({ requiresTwoFactor: true });
    await settle();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#login-code')).not.toBeNull();

    setValue(fixture, '#login-code', '654321');
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    httpController.expectOne('/api/auth/two-factor/verify').flush({ requiresTwoFactor: false });
    await settle();

    expect(authService.state()).toBe('authenticated');
    expect(settingsLoadSpy).toHaveBeenCalled();
  });

  it('should switch back to the credentials step when the challenge expired', async () => {
    const fixture = await createPage(true);

    setValue(fixture, '#login-code', '000000');
    (fixture.nativeElement.querySelector('form button[type="submit"]') as HTMLButtonElement).click();

    httpController
      .expectOne('/api/auth/two-factor/verify')
      .flush({ detail: 'Gone.', code: 'two_factor_challenge_invalid' }, { status: 410, statusText: 'Gone' });
    await settle();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#login-username')).not.toBeNull();
  });
  it('navigates to setup when retrying a failed initial probe discovers no account', async () => {
    const initial = authService.initializeAsync();
    httpController.expectOne('/api/auth/state').flush({}, { status: 503, statusText: 'Unavailable' });
    await initial;
    const fixture = await createPage(false);
    const retry = fixture.componentInstance['retryStateProbeAsync']();
    httpController.expectOne('/api/auth/state').flush({ setupRequired: true, authenticated: false });
    await retry;
    expect(TestBed.inject(Router).url).toBe('/setup');
    expect(settingsLoadSpy).not.toHaveBeenCalled();
  });

  it('finishes login when retrying a failed initial probe discovers a valid session', async () => {
    const fixture = await createPage(false);
    const retry = fixture.componentInstance['retryStateProbeAsync']();
    httpController.expectOne('/api/auth/state').flush({ setupRequired: false, authenticated: true });
    await retry;
    expect(TestBed.inject(Router).url).toBe('/library');
    expect(settingsLoadSpy).toHaveBeenCalledTimes(1);
  });
});
