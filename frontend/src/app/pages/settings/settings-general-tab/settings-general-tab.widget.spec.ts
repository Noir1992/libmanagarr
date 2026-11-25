import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TitleSortingSettingsCardWidget } from './title-sorting-settings-card/title-sorting-settings-card.widget';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { AppLanguage } from '../../../interfaces/general/app-language';
import { AppTheme } from '../../../interfaces/general/app-theme';
import { AuthProtectionSettings } from '../../../interfaces/settings/auth-protection-settings';
import { LoggingSettings } from '../../../interfaces/settings/logging-settings';
import { AppI18nService } from '../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../services/app-settings.service';
import { SettingsGeneralTabWidget } from './settings-general-tab.widget';

describe('SettingsGeneralTabWidget', () => {
  let fixture: ComponentFixture<SettingsGeneralTabWidget>;

  const i18nStub: Pick<AppI18nService, 't' | 'tForLanguage'> = {
    t: (key: string): string =>
      ({
        'settings.general': 'General',
        'settings.general.description': 'General settings',
        'settings.language': 'Language',
        'settings.general.language.info': 'Language info',
        'settings.theme': 'Theme',
        'settings.general.theme.info': 'Theme info',
        'settings.language.self': 'English',
        'theme.light': 'Light',
        'theme.dark': 'Dark',
        'theme.system': 'System',
        'settings.saveError': 'Save error',
        'settings.authProtection.title': 'Sign-in protection',
        'settings.authProtection.requestsPerMinute': 'Protected requests per minute',
        'settings.authProtection.requestsPerMinute.info': 'Protected requests per minute info',
        'settings.authProtection.maxFailedAttempts': 'Failed attempts before the account lock',
        'settings.authProtection.maxFailedAttempts.info': 'Failed attempts info',
        'settings.authProtection.saving': 'Saving',
        'settings.authProtection.validation.required': 'This value is required.',
        'settings.authProtection.validation.range': 'Enter a value within the allowed range.',
        'settings.authProtection.validation.invalid': 'Enter a valid value.',
        'settings.logs.title': 'Logs',
        'settings.logs.path': 'Log path',
        'settings.logs.path.info': 'Log path info',
        'settings.logs.retentionDays': 'Retention days',
        'settings.logs.retentionDays.info': 'Retention days info',
        'settings.logs.minimumLevel': 'Minimum level',
        'settings.logs.minimumLevel.info': 'Minimum level info',
        'settings.logs.validation.required': 'This value is required.',
        'settings.logs.validation.range': 'Enter a value within the allowed range.',
        'settings.logs.validation.invalid': 'Enter a valid value.',
        'settings.logs.saving': 'Saving',
        'common.info': 'info',
      })[key] ?? key,
    tForLanguage: (_language: AppLanguage, key: string): string =>
      ({
        'settings.language.self': 'English',
      })[key] ?? key,
    setPreviewLanguage: vi.fn(),
  } as Pick<AppI18nService, 't' | 'tForLanguage'>;

  beforeEach(async () => {
    const languageSignal: WritableSignal<AppLanguage> = signal<AppLanguage>('en');
    const themeSignal: WritableSignal<AppTheme> = signal<AppTheme>('system');
    const loggingSignal: WritableSignal<LoggingSettings> = signal<LoggingSettings>({
      path: './logs',
      retentionDays: 30,
      minimumLevel: 'Information',
    });
    const authProtectionSignal: WritableSignal<AuthProtectionSettings> = signal<AuthProtectionSettings>({
      requestsPerMinute: 10,
      maxFailedAttempts: 5,
    });
    const settingsServiceStub: Pick<
      AppSettingsService,
      | 'loadLoggingSettings'
      | 'saveGeneral'
      | 'saveLoggingSettings'
      | 'saveAuthProtection'
      | 'logging'
      | 'authProtection'
      | 'language'
      | 'theme'
      | 'titleSorting'
    > = {
      loadLoggingSettings: vi.fn().mockResolvedValue(undefined),
      saveGeneral: vi.fn().mockImplementation(async (value: { language: AppLanguage; theme: AppTheme }) => {
        languageSignal.set(value.language);
        themeSignal.set(value.theme);
      }),
      saveLoggingSettings: vi.fn().mockResolvedValue(undefined),
      saveAuthProtection: vi.fn().mockImplementation(async (value: AuthProtectionSettings) => {
        authProtectionSignal.set(value);
      }),
      logging: loggingSignal,
      authProtection: authProtectionSignal,
      language: languageSignal,
      theme: themeSignal,
      titleSorting: signal({ enabled: false, ignoredLeadingWords: [] }),
    };
    const dialogStub: Pick<MatDialog, 'open'> = {
      open: vi.fn().mockReturnValue({
        afterClosed: () => of(true),
      }),
    };

    await TestBed.configureTestingModule({
      imports: [SettingsGeneralTabWidget],
      providers: [
        { provide: AppI18nService, useValue: i18nStub },
        { provide: AppSettingsService, useValue: settingsServiceStub },
        { provide: MatDialog, useValue: dialogStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsGeneralTabWidget);
    fixture.detectChanges();
  });

  it('renders the general tab labels', () => {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.textContent).toContain('General');
    expect(compiled.textContent).toContain('General settings');
    expect(compiled.textContent).toContain('Language');
    expect(compiled.textContent).toContain('Theme');
  });

  it('autosaves general changes through the backend service', async () => {
    const settingsService = vi.mocked(TestBed.inject(AppSettingsService));

    fixture.componentInstance['settingsForm'].controls.language.setValue('de');
    await new Promise((resolve) => setTimeout(resolve, 350));

    expect(settingsService.saveGeneral).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance['generalFieldFeedbackState']().language).toBe('success');
  });

  it('shows a server validation error below the language select', async () => {
    const settingsService = vi.mocked(TestBed.inject(AppSettingsService));
    settingsService.saveGeneral.mockRejectedValue(
      new ValidationProblemError('Validation failed', {
        'settings.language': ['Language is already used.'],
      }),
    );

    fixture.componentInstance['settingsForm'].controls.language.setValue('de');
    await new Promise((resolve) => setTimeout(resolve, 350));
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance['fieldFeedbackMessage']('language')).toBe('Language is already used.');
    expect(compiled.querySelector('#language-select.has-error')).toBeTruthy();
    expect(compiled.querySelector('.field-error')).toBeTruthy();
    expect(compiled.textContent).toContain('Language is already used.');
  });

  it('discards pending general changes when navigation is confirmed', () => {
    const confirmTarget: typeof globalThis & {
      confirm: (message?: string) => boolean;
    } = globalThis as typeof globalThis & {
      confirm: (message?: string) => boolean;
    };
    vi.spyOn(confirmTarget, 'confirm').mockReturnValue(true);

    fixture.componentInstance['settingsForm'].controls.language.setValue('de');
    fixture.componentInstance['settingsForm'].controls.language.markAsDirty();

    expect(fixture.componentInstance.canDeactivate()).toBe(true);
    expect(fixture.componentInstance['settingsForm'].controls.language.getRawValue()).toBe('en');
    expect(fixture.componentInstance['settingsForm'].dirty).toBe(false);
  });

  it('protects and discards unsaved title rules on navigation', () => {
    const card = fixture.debugElement.query(By.directive(TitleSortingSettingsCardWidget))
      .componentInstance as TitleSortingSettingsCardWidget;
    card['form'].controls.ignoredLeadingWords.setValue('A');
    card['form'].controls.ignoredLeadingWords.markAsDirty();
    const confirm = vi.spyOn(globalThis, 'confirm').mockReturnValue(false);
    expect(fixture.componentInstance.canDeactivate()).toBe(false);
    expect(confirm.mock.lastCall![0]).toContain('settings.titleSorting.title');
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('A');
    confirm.mockReturnValue(true);
    expect(fixture.componentInstance.canDeactivate()).toBe(true);
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('');
    expect(card.hasUnsavedChanges()).toBe(false);
  });

  it('blocks navigation while title rules are saving without prompting', () => {
    const card = fixture.debugElement.query(By.directive(TitleSortingSettingsCardWidget))
      .componentInstance as TitleSortingSettingsCardWidget;
    card['saving'].set(true);
    const confirm = vi.spyOn(globalThis, 'confirm');
    expect(fixture.componentInstance.canDeactivate()).toBe(false);
    expect(confirm).not.toHaveBeenCalled();
    card['saving'].set(false);
    expect(fixture.componentInstance.canDeactivate()).toBe(true);
  });
});
