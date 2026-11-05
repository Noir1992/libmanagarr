import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { signal, WritableSignal } from '@angular/core';
import { AuthProtectionSettings } from '../../../../interfaces/settings/auth-protection-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { AuthProtectionSettingsCardWidget } from './auth-protection-settings-card.widget';

describe('AuthProtectionSettingsCardWidget', () => {
  let fixture: ComponentFixture<AuthProtectionSettingsCardWidget>;
  let saveAuthProtectionSpy: jasmine.Spy;
  let authProtectionSignal: WritableSignal<AuthProtectionSettings>;

  beforeEach(async () => {
    authProtectionSignal = signal<AuthProtectionSettings>({
      requestsPerMinute: 10,
      maxFailedAttempts: 5,
    });
    saveAuthProtectionSpy = jasmine.createSpy('saveAuthProtection').and.callFake(async (authProtection: AuthProtectionSettings) => {
      authProtectionSignal.set(authProtection);
    });

    await TestBed.configureTestingModule({
      imports: [ReactiveFormsModule, AuthProtectionSettingsCardWidget],
      providers: [
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string): string =>
              ({
                'settings.authProtection.title': 'Sign-in protection',
                'settings.authProtection.requestsPerMinute': 'Protected requests per minute',
                'settings.authProtection.requestsPerMinute.info': 'Protected requests per minute info',
                'settings.authProtection.maxFailedAttempts': 'Failed attempts before the account lock',
                'settings.authProtection.maxFailedAttempts.info': 'Failed attempts info',
                'settings.authProtection.validation.required': 'This value is required.',
                'settings.authProtection.validation.range': 'Enter a value within the allowed range.',
                'settings.authProtection.validation.invalid': 'Enter a valid value.',
                'settings.authProtection.saving': 'Saving',
                'settings.saveError': 'Save error',
                'common.info': 'info',
              })[key] ?? key,
          },
        },
        {
          provide: AppSettingsService,
          useValue: {
            saveAuthProtection: saveAuthProtectionSpy,
            authProtection: authProtectionSignal,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AuthProtectionSettingsCardWidget);
    fixture.detectChanges();
  });

  it('renders both limits with their stored values', () => {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.textContent).toContain('Sign-in protection');
    expect(compiled.textContent).toContain('Protected requests per minute');
    expect(compiled.textContent).toContain('Failed attempts before the account lock');
    expect((compiled.querySelector('#auth-protection-requests') as HTMLInputElement).value).toBe('10');
    expect((compiled.querySelector('#auth-protection-attempts') as HTMLInputElement).value).toBe('5');
  });

  it('saves one changed limit and keeps the other stored value', async () => {
    fixture.componentInstance['authProtectionForm'].controls.requestsPerMinute.setValue(25);
    fixture.componentInstance['authProtectionForm'].controls.requestsPerMinute.markAsDirty();
    await fixture.componentInstance['onSaveAuthProtectionFieldAsync']('requestsPerMinute');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(saveAuthProtectionSpy).toHaveBeenCalledOnceWith({ requestsPerMinute: 25, maxFailedAttempts: 5 });
    expect(fixture.componentInstance['authProtectionFieldFeedback']('requestsPerMinute')).toBe('success');
  });

  it('refuses a value above the allowed range before it reaches the backend', async () => {
    fixture.componentInstance['authProtectionForm'].controls.requestsPerMinute.setValue(5000);
    fixture.componentInstance['authProtectionForm'].controls.requestsPerMinute.markAsDirty();
    await fixture.componentInstance['onSaveAuthProtectionFieldAsync']('requestsPerMinute');
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(saveAuthProtectionSpy).not.toHaveBeenCalled();
    expect(compiled.querySelector('#auth-protection-requests.has-error')).toBeTruthy();
    expect(compiled.textContent).toContain('Enter a value within the allowed range.');
  });

  it('refuses zero, which would switch the protection off', async () => {
    fixture.componentInstance['authProtectionForm'].controls.maxFailedAttempts.setValue(0);
    fixture.componentInstance['authProtectionForm'].controls.maxFailedAttempts.markAsDirty();
    await fixture.componentInstance['onSaveAuthProtectionFieldAsync']('maxFailedAttempts');
    fixture.detectChanges();

    expect(saveAuthProtectionSpy).not.toHaveBeenCalled();
    expect(fixture.componentInstance['authProtectionFieldError']('maxFailedAttempts')).toBe('Enter a value within the allowed range.');
  });

  it('shows a server validation error below the rejected input', async () => {
    saveAuthProtectionSpy.and.rejectWith(
      new ValidationProblemError('Validation failed', {
        'authProtection.maxFailedAttempts': ['The failed attempts must be between 1 and 100.'],
      }),
    );

    fixture.componentInstance['authProtectionForm'].controls.maxFailedAttempts.setValue(8);
    fixture.componentInstance['authProtectionForm'].controls.maxFailedAttempts.markAsDirty();
    await fixture.componentInstance['onSaveAuthProtectionFieldAsync']('maxFailedAttempts');
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance['authProtectionFieldError']('maxFailedAttempts')).toBe(
      'The failed attempts must be between 1 and 100.',
    );
    expect(compiled.querySelector('#auth-protection-attempts.has-error')).toBeTruthy();
  });

  it('keeps an unsaved value until it is discarded', () => {
    fixture.componentInstance['authProtectionForm'].controls.maxFailedAttempts.setValue(9);
    fixture.componentInstance['authProtectionForm'].controls.maxFailedAttempts.markAsDirty();

    expect(fixture.componentInstance.hasUnsavedChanges()).toBeTrue();

    fixture.componentInstance.discardUnsavedChanges();

    expect(fixture.componentInstance.hasUnsavedChanges()).toBeFalse();
    expect(fixture.componentInstance['authProtectionForm'].controls.maxFailedAttempts.getRawValue()).toBe(5);
  });
});
