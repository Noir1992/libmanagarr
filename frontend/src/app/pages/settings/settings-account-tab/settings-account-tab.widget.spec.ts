import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../services/auth.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AbstractControl, FormGroup } from '@angular/forms';
import { AccountBackend } from '../../../interfaces/auth/account-backend';
import { AppI18nService } from '../../../services/app-i18n.service';
import { SettingsAccountTabWidget } from './settings-account-tab.widget';

interface PasswordFieldErrorsProbe {
  readonly currentPassword: string | null;
  readonly newPassword: string | null;
  readonly confirmPassword: string | null;
}

interface AccountTabProbe {
  account: Signal<AccountBackend | null>;
  passwordChanged: Signal<boolean>;
  passwordFieldErrors: Signal<PasswordFieldErrorsProbe>;
  enableStep: Signal<'idle' | 'scan'>;
  issuedRecoveryCodes: Signal<string[] | null>;
  passwordForm: FormGroup;
  enableForm: FormGroup;
  disableForm: FormGroup;
  rotateForm: FormGroup;
  changePasswordAsync(): Promise<void>;
  startTwoFactorSetupAsync(): Promise<void>;
  confirmTwoFactorSetupAsync(): Promise<void>;
  disableTwoFactorAsync(): Promise<void>;
  rotateRecoveryCodesAsync(): Promise<void>;
}

const DISABLED_ACCOUNT: AccountBackend = { username: 'admin', twoFactorEnabled: false, recoveryCodesRemaining: 0 };
const ENABLED_ACCOUNT: AccountBackend = { username: 'admin', twoFactorEnabled: true, recoveryCodesRemaining: 10 };

describe('SettingsAccountTabWidget', () => {
  let httpController: HttpTestingController;
  let router: jasmine.SpyObj<Router>;

  const i18nSpy: jasmine.SpyObj<AppI18nService> = jasmine.createSpyObj<AppI18nService>('AppI18nService', ['t', 'tCount']);

  i18nSpy.t.and.callFake((key: string): string => key);
  i18nSpy.tCount.and.callFake((count: number, one: string, plural: string): string => (count === 1 ? one : plural));

  beforeEach(() => {
    router = jasmine.createSpyObj<Router>('Router', ['navigateByUrl']);
    router.navigateByUrl.and.resolveTo(true);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AppI18nService, useValue: i18nSpy },
        { provide: Router, useValue: router },
      ],
    });

    httpController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpController.verify();
  });

  async function settle(): Promise<void> {
    await new Promise<void>((resolve: () => void): void => {
      setTimeout(resolve);
    });
  }

  async function createWidget(account: AccountBackend): Promise<ComponentFixture<SettingsAccountTabWidget>> {
    const fixture = TestBed.createComponent(SettingsAccountTabWidget);

    fixture.detectChanges();
    httpController.expectOne('/api/account').flush(account);
    await settle();
    fixture.detectChanges();

    return fixture;
  }

  function probeOf(fixture: ComponentFixture<SettingsAccountTabWidget>): AccountTabProbe {
    return fixture.componentInstance as unknown as AccountTabProbe;
  }

  async function flushAccountReload(account: AccountBackend): Promise<void> {
    await settle();
    httpController.expectOne('/api/account').flush(account);
  }

  it('applies the setup screen password rules to the change-password form', async () => {
    const fixture = await createWidget(DISABLED_ACCOUNT);
    const probe: AccountTabProbe = probeOf(fixture);
    const newPassword: AbstractControl = probe.passwordForm.controls['newPassword'];
    const confirmPassword: AbstractControl = probe.passwordForm.controls['confirmPassword'];

    newPassword.setValue('short');
    newPassword.markAsDirty();
    confirmPassword.setValue('short');
    fixture.detectChanges();

    expect(newPassword.hasError('minlength')).toBeTrue();
    expect(fixture.nativeElement.querySelector('.field-error')?.textContent ?? null).toContain('auth.passwordLength');

    confirmPassword.setValue('a-different-long-enough-password');
    fixture.detectChanges();

    expect(probe.passwordForm.hasError('passwordMismatch')).toBeTrue();

    probe.passwordForm.controls['currentPassword'].setValue('existing-password');
    newPassword.setValue('a-sufficiently-long-password');
    confirmPassword.setValue('a-sufficiently-long-password');
    fixture.detectChanges();

    expect(probe.passwordForm.hasError('passwordMismatch')).toBeFalse();
    expect(probe.passwordForm.valid).toBeTrue();
  });

  it('should load the account profile when the tab opens', async () => {
    const fixture = await createWidget(DISABLED_ACCOUNT);

    const usernameInput: HTMLInputElement = fixture.nativeElement.querySelector('#account-username');

    expect(usernameInput.value).toBe('admin');
    expect(probeOf(fixture).account()?.twoFactorEnabled).toBeFalse();
  });

  it('should change the password and ask for a fresh login', async () => {
    const fixture = await createWidget(DISABLED_ACCOUNT);
    const page: AccountTabProbe = probeOf(fixture);

    page.passwordForm.patchValue({
      currentPassword: 'old-password',
      newPassword: 'new-password-value',
      confirmPassword: 'new-password-value',
    });

    const change: Promise<void> = page.changePasswordAsync();
    const request = httpController.expectOne('/api/account/password');

    expect(request.request.body).toEqual({
      currentPassword: 'old-password',
      newPassword: 'new-password-value',
      confirmPassword: 'new-password-value',
      code: undefined,
      useRecoveryCode: false,
    });

    request.flush(null);
    await change;
    await fixture.whenStable();

    expect(page.passwordChanged()).toBeTrue();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login', { replaceUrl: true });
    expect(TestBed.inject(AuthService).state()).toBe('anonymous');
  });

  it('should map validation failures onto the password fields', async () => {
    const fixture = await createWidget(DISABLED_ACCOUNT);
    const page: AccountTabProbe = probeOf(fixture);

    page.passwordForm.patchValue({
      currentPassword: 'old-password',
      newPassword: 'a-long-enough-old-password',
      confirmPassword: 'a-long-enough-old-password',
    });

    const change: Promise<void> = page.changePasswordAsync();

    httpController
      .expectOne('/api/account/password')
      .flush(
        { status: 400, title: 'One or more validation errors occurred.', errors: { newPassword: ['password_length'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await change;
    await fixture.whenStable();

    expect(page.passwordFieldErrors().newPassword).toBe('auth.passwordLength');
    expect(page.passwordChanged()).toBeFalse();
  });

  it('should start the two-factor setup and switch to the scan step', async () => {
    const fixture = await createWidget(DISABLED_ACCOUNT);
    const page: AccountTabProbe = probeOf(fixture);

    const start: Promise<void> = page.startTwoFactorSetupAsync();
    const request = httpController.expectOne('/api/account/two-factor/setup');

    expect(request.request.body).toBeNull();
    request.flush({
      sharedKey: 'SHARED-KEY',
      authenticatorUri: 'otpauth://totp/LibManagarr:admin',
      qrCodeSvg: '<svg></svg>',
      expiresAt: '2026-10-19T12:10:00Z',
    });
    await start;
    await fixture.whenStable();

    expect(page.enableStep()).toBe('scan');
  });

  it('should confirm the setup, keep the issued codes once and refresh the account', async () => {
    const fixture = await createWidget(DISABLED_ACCOUNT);
    const page: AccountTabProbe = probeOf(fixture);

    page.enableForm.patchValue({ code: '123456' });

    const confirm: Promise<void> = page.confirmTwoFactorSetupAsync();
    const request = httpController.expectOne('/api/account/two-factor/setup/confirm');

    expect(request.request.body).toEqual({ code: '123456' });
    request.flush({ recoveryCodes: ['recover-code-one'] });
    await flushAccountReload(ENABLED_ACCOUNT);
    await confirm;
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page.issuedRecoveryCodes()?.length).toBe(1);
    expect(page.enableStep()).toBe('idle');
    expect(page.account()?.twoFactorEnabled).toBeTrue();
  });

  it('should disable two factor authentication after the confirmation', async () => {
    const fixture = await createWidget(ENABLED_ACCOUNT);
    const page: AccountTabProbe = probeOf(fixture);
    const confirmSpy: jasmine.Spy = spyOn(globalThis, 'confirm').and.returnValue(true);

    page.disableForm.patchValue({ currentPassword: 'old-password' });

    const disable: Promise<void> = page.disableTwoFactorAsync();
    const request = httpController.expectOne('/api/account/two-factor/disable');

    expect(confirmSpy).toHaveBeenCalled();
    expect(request.request.body).toEqual({ currentPassword: 'old-password', code: undefined, useRecoveryCode: false });
    request.flush(null);
    await flushAccountReload(DISABLED_ACCOUNT);
    await disable;

    expect(page.account()?.twoFactorEnabled).toBeFalse();
  });

  it('should not call the api when the disable confirmation is declined', async () => {
    const fixture = await createWidget(ENABLED_ACCOUNT);
    const page: AccountTabProbe = probeOf(fixture);

    spyOn(globalThis, 'confirm').and.returnValue(false);
    page.disableForm.patchValue({ currentPassword: 'old-password' });

    await page.disableTwoFactorAsync();

    httpController.expectNone('/api/account/two-factor/disable');
    expect(page.account()?.twoFactorEnabled).toBeTrue();
  });

  it('should rotate the recovery codes and show them once again', async () => {
    const fixture = await createWidget(ENABLED_ACCOUNT);
    const page: AccountTabProbe = probeOf(fixture);

    page.rotateForm.patchValue({ currentPassword: 'old-password', code: 'recover-code-one' });

    const rotate: Promise<void> = page.rotateRecoveryCodesAsync();
    const request = httpController.expectOne('/api/account/two-factor/recovery-codes');

    expect(request.request.body).toEqual({ currentPassword: 'old-password', code: 'recover-code-one', useRecoveryCode: false });
    request.flush({ recoveryCodes: ['fresh-code-one', 'fresh-code-two'] });
    await flushAccountReload(ENABLED_ACCOUNT);
    await rotate;

    expect(page.issuedRecoveryCodes()?.length).toBe(2);
  });
  it('requires confirmation before leaving with unsaved recovery codes', async () => {
    const fixture = await createWidget(ENABLED_ACCOUNT);
    const codes = probeOf(fixture).issuedRecoveryCodes as WritableSignal<string[] | null>;
    codes.set(['save-this-code']);
    const confirm = spyOn(globalThis, 'confirm').and.returnValue(false);

    expect(fixture.componentInstance.canDeactivate()).toBeFalse();
    expect(confirm).toHaveBeenCalledWith('settings.account.recoveryCodesWarning');
    expect(codes()).toEqual(['save-this-code']);
    confirm.and.returnValue(true);
    expect(fixture.componentInstance.canDeactivate()).toBeTrue();
    expect(codes()).toBeNull();
  });
});
