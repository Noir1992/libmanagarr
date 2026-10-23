import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TitleSortingSettingsCardWidget } from './title-sorting-settings-card/title-sorting-settings-card.widget';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { AppLanguage } from '../../../interfaces/general/app-language';
import { AppTheme } from '../../../interfaces/general/app-theme';
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
    setPreviewLanguage: jasmine.createSpy('setPreviewLanguage'),
  } as Pick<AppI18nService, 't' | 'tForLanguage'>;

  beforeEach(async () => {
    const languageSignal: WritableSignal<AppLanguage> = signal<AppLanguage>('en');
    const themeSignal: WritableSignal<AppTheme> = signal<AppTheme>('system');
    const loggingSignal: WritableSignal<LoggingSettings> = signal<LoggingSettings>({
      path: './logs',
      retentionDays: 30,
      minimumLevel: 'Information',
    });
    const settingsServiceStub: Pick<
      AppSettingsService,
      'loadLoggingSettings' | 'saveGeneral' | 'saveLoggingSettings' | 'logging' | 'language' | 'theme' | 'titleSorting'
    > = {
      loadLoggingSettings: jasmine.createSpy('loadLoggingSettings').and.resolveTo(),
      saveGeneral: jasmine.createSpy('saveGeneral').and.callFake(async (value: { language: AppLanguage; theme: AppTheme }) => {
        languageSignal.set(value.language);
        themeSignal.set(value.theme);
      }),
      saveLoggingSettings: jasmine.createSpy('saveLoggingSettings').and.resolveTo(),
      logging: loggingSignal,
      language: languageSignal,
      theme: themeSignal,
      titleSorting: signal({ enabled: false, ignoredLeadingWords: [] }),
    };
    const dialogStub: Pick<MatDialog, 'open'> = {
      open: jasmine.createSpy('open').and.returnValue({
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
    const settingsService: jasmine.SpyObj<AppSettingsService> = TestBed.inject(AppSettingsService) as jasmine.SpyObj<AppSettingsService>;

    fixture.componentInstance['settingsForm'].controls.language.setValue('de');
    await new Promise((resolve) => setTimeout(resolve, 350));

    expect(settingsService.saveGeneral).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance['generalFieldFeedbackState']().language).toBe('success');
  });

  it('shows a server validation error below the language select', async () => {
    const settingsService: jasmine.SpyObj<AppSettingsService> = TestBed.inject(AppSettingsService) as jasmine.SpyObj<AppSettingsService>;
    settingsService.saveGeneral.and.rejectWith(
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
    spyOn(confirmTarget, 'confirm').and.returnValue(true);

    fixture.componentInstance['settingsForm'].controls.language.setValue('de');
    fixture.componentInstance['settingsForm'].controls.language.markAsDirty();

    expect(fixture.componentInstance.canDeactivate()).toBeTrue();
    expect(fixture.componentInstance['settingsForm'].controls.language.getRawValue()).toBe('en');
    expect(fixture.componentInstance['settingsForm'].dirty).toBeFalse();
  });

  it('protects and discards unsaved title rules on navigation', () => {
    const card = fixture.debugElement.query(By.directive(TitleSortingSettingsCardWidget))
      .componentInstance as TitleSortingSettingsCardWidget;
    card['form'].controls.ignoredLeadingWords.setValue('A');
    card['form'].controls.ignoredLeadingWords.markAsDirty();
    const confirm = spyOn(globalThis, 'confirm').and.returnValue(false);
    expect(fixture.componentInstance.canDeactivate()).toBeFalse();
    expect(confirm.calls.mostRecent().args[0]).toContain('settings.titleSorting.title');
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('A');
    confirm.and.returnValue(true);
    expect(fixture.componentInstance.canDeactivate()).toBeTrue();
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('');
    expect(card.hasUnsavedChanges()).toBeFalse();
  });

  it('blocks navigation while title rules are saving without prompting', () => {
    const card = fixture.debugElement.query(By.directive(TitleSortingSettingsCardWidget))
      .componentInstance as TitleSortingSettingsCardWidget;
    card['saving'].set(true);
    const confirm = spyOn(globalThis, 'confirm');
    expect(fixture.componentInstance.canDeactivate()).toBeFalse();
    expect(confirm).not.toHaveBeenCalled();
    card['saving'].set(false);
    expect(fixture.componentInstance.canDeactivate()).toBeTrue();
  });
});
