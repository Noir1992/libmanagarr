import { DOCUMENT, registerLocaleData } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import localeDe from '@angular/common/locales/de';
import localeEn from '@angular/common/locales/en';
import localeFr from '@angular/common/locales/fr';
import { computed, inject, Injectable, Signal, signal, WritableSignal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppLanguage } from '../interfaces/general/app-language';
import { ARR_APPLICATION_KINDS, ArrApplicationKind } from '../interfaces/general/arr-application-kind';
import { ArrInstanceSettings } from '../interfaces/settings/arr-instance-settings';
import { GeneralSettingsBackend } from '../interfaces/settings/general-settings-backend';
import { InstancesSettingsBackend } from '../interfaces/settings/instances-settings-backend';
import { LoggingSettings } from '../interfaces/settings/logging-settings';
import { LoggingSettingsUpdateRequestBackend } from '../interfaces/settings/logging-settings-update-request-backend';
import { SeerrInstanceSettings } from '../interfaces/settings/seerr-instance-settings';
import { SyncArrInstanceResult } from '../interfaces/settings/sync-arr-instance-result';
import { TestArrInstanceConnectionResult } from '../interfaces/settings/test-arr-instance-connection-result';
import { AppTheme } from '../interfaces/general/app-theme';
import { TitleSortingSettings } from '../interfaces/settings/title-sorting-settings';
import { AppI18nService } from './app-i18n.service';

export class ValidationProblemError extends Error {
  constructor(
    message: string,
    readonly fieldErrors: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'ValidationProblemError';
  }
}

const DEFAULT_LANGUAGE: AppLanguage = 'en';
const DEFAULT_THEME: AppTheme = 'system';

const DEFAULT_LOGGING: LoggingSettings = {
  path: './logs',
  retentionDays: 30,
  minimumLevel: 'Information',
};

const DEFAULT_SONARR_INSTANCES: ArrInstanceSettings[] = [];
const DEFAULT_RADARR_INSTANCES: ArrInstanceSettings[] = [];

const APP_LANGUAGE_TO_BACKEND_LANGUAGE: Record<AppLanguage, GeneralSettingsBackend['language']> = {
  en: 'En',
  de: 'De',
  fr: 'Fr',
};

export const BACKEND_LANGUAGE_TO_APP_LANGUAGE: Record<GeneralSettingsBackend['language'], AppLanguage> = {
  En: 'en',
  De: 'de',
  Fr: 'fr',
};

const APP_THEME_TO_BACKEND_THEME: Record<AppTheme, GeneralSettingsBackend['theme']> = {
  light: 'Light',
  dark: 'Dark',
  system: 'System',
};

const BACKEND_THEME_TO_APP_THEME: Record<GeneralSettingsBackend['theme'], AppTheme> = {
  Light: 'light',
  Dark: 'dark',
  System: 'system',
};

type SyncInstanceResponseDto = Partial<{
  message: string;
  syncedAt: string | null;
  itemCount: number;
}>;

@Injectable({ providedIn: 'root' })
export class AppSettingsService {
  private readonly http: HttpClient = inject(HttpClient);
  private readonly document: Document = inject(DOCUMENT);
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly loadErrorState: WritableSignal<string | null> = signal<string | null>(null);
  private readonly loadedState: WritableSignal<boolean> = signal<boolean>(false);
  private readonly syncingArrInstanceKeys: WritableSignal<ReadonlySet<string>> = signal<ReadonlySet<string>>(new Set<string>());
  private readonly languageState: WritableSignal<AppLanguage> = signal<AppLanguage>(DEFAULT_LANGUAGE);
  private readonly themeState: WritableSignal<AppTheme> = signal<AppTheme>(DEFAULT_THEME);
  private readonly titleSortingState = signal<TitleSortingSettings>({ enabled: false, ignoredLeadingWords: [] });
  private readonly sonarrInstancesState: WritableSignal<ArrInstanceSettings[]> = signal<ArrInstanceSettings[]>(DEFAULT_SONARR_INSTANCES);
  private readonly radarrInstancesState: WritableSignal<ArrInstanceSettings[]> = signal<ArrInstanceSettings[]>(DEFAULT_RADARR_INSTANCES);
  private readonly seerrInstanceState: WritableSignal<SeerrInstanceSettings | null> = signal<SeerrInstanceSettings | null>(null);
  private readonly loggingState: WritableSignal<LoggingSettings> = signal<LoggingSettings>(DEFAULT_LOGGING);
  private loadingPromise?: Promise<void>;
  private readonly apiUrl: string = '/api/settings/';
  readonly language: Signal<AppLanguage> = computed((): AppLanguage => this.languageState());
  readonly theme: Signal<AppTheme> = computed((): AppTheme => this.themeState());
  readonly titleSorting = this.titleSortingState.asReadonly();
  readonly sonarrInstances: Signal<ArrInstanceSettings[]> = computed((): ArrInstanceSettings[] => this.sonarrInstancesState());
  readonly radarrInstances: Signal<ArrInstanceSettings[]> = computed((): ArrInstanceSettings[] => this.radarrInstancesState());
  readonly seerrInstance: Signal<SeerrInstanceSettings | null> = computed((): SeerrInstanceSettings | null => this.seerrInstanceState());
  readonly logging: Signal<LoggingSettings> = computed((): LoggingSettings => this.loggingState());
  readonly loadError: Signal<string | null> = computed((): string | null => this.loadErrorState());

  async load(): Promise<void> {
    if (this.loadedState()) {
      return;
    }

    if (this.loadingPromise) {
      return this.loadingPromise;
    }

    this.loadingPromise = this.loadBaseSettingsInternalAsync();

    return this.loadingPromise;
  }

  localeId(): string {
    switch (this.languageState()) {
      case 'de':
        return 'de';
      case 'fr':
        return 'fr';
      default:
        return 'en-US';
    }
  }

  /**
   * Applies the fallback (system) theme for unauthenticated screens, which never load the
   * account-bound general settings; keeps login and setup on the operating-system preference.
   */
  applyDefaultTheme(): void {
    this.applyTheme(DEFAULT_THEME);
  }

  async loadInstances(): Promise<void> {
    try {
      const backendSettings: InstancesSettingsBackend = await firstValueFrom(
        this.http.get<InstancesSettingsBackend>(`${this.apiUrl}instances`),
      );

      this.sonarrInstancesState.set(backendSettings.sonarrInstances);
      this.radarrInstancesState.set(backendSettings.radarrInstances);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.loadError'));
    }
  }

  async loadSeerrInstance(): Promise<void> {
    try {
      const instance: SeerrInstanceSettings | null = await firstValueFrom(
        this.http.get<SeerrInstanceSettings | null>(`${this.apiUrl}seerr`),
      );

      this.seerrInstanceState.set(instance ?? null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.loadError'));
    }
  }

  async loadLoggingSettings(): Promise<void> {
    try {
      const logging: LoggingSettings = await firstValueFrom(this.http.get<LoggingSettings>(`${this.apiUrl}logging`));

      this.loggingState.set(logging);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.loadError'));
    }
  }

  async saveGeneral(settings: { language: AppLanguage; theme: AppTheme }): Promise<void> {
    try {
      await firstValueFrom(
        this.http.put<void>(`${this.apiUrl}general`, {
          language: APP_LANGUAGE_TO_BACKEND_LANGUAGE[settings.language],
          theme: APP_THEME_TO_BACKEND_THEME[settings.theme],
        }),
      );

      this.applyLanguage(settings.language);
      this.applyTheme(settings.theme);
      this.languageState.set(settings.language);
      this.themeState.set(settings.theme);
      this.loadErrorState.set(null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.saveError'));
    }
  }

  async saveTitleSorting(settings: TitleSortingSettings): Promise<void> {
    try {
      const saved = await firstValueFrom(
        this.http.put<TitleSortingSettings>(`${this.apiUrl}title-sorting`, {
          enabled: settings.enabled,
          ignoredLeadingWords: [...settings.ignoredLeadingWords],
        }),
      );
      this.titleSortingState.set({ enabled: saved.enabled, ignoredLeadingWords: [...saved.ignoredLeadingWords] });
      this.loadErrorState.set(null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.saveError'));
    }
  }

  async saveSonarrInstance(index: number, instance: ArrInstanceSettings): Promise<void> {
    await this.saveInstanceScopedAsync(ARR_APPLICATION_KINDS.SONARR, index, instance);
  }

  async saveRadarrInstance(index: number, instance: ArrInstanceSettings): Promise<void> {
    await this.saveInstanceScopedAsync(ARR_APPLICATION_KINDS.RADARR, index, instance);
  }

  async deleteSonarrInstance(index: number): Promise<void> {
    await this.deleteInstanceScopedAsync(ARR_APPLICATION_KINDS.SONARR, index);
  }

  async deleteRadarrInstance(index: number): Promise<void> {
    await this.deleteInstanceScopedAsync(ARR_APPLICATION_KINDS.RADARR, index);
  }

  async saveSeerrInstance(instance: SeerrInstanceSettings): Promise<void> {
    try {
      await firstValueFrom(this.http.put<void>(`${this.apiUrl}seerr`, instance));
      this.seerrInstanceState.set(instance);
      this.loadErrorState.set(null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.saveError'));
    }
  }

  async deleteSeerrInstance(): Promise<void> {
    try {
      await firstValueFrom(this.http.delete<void>(`${this.apiUrl}seerr`));
      this.seerrInstanceState.set(null);
      this.loadErrorState.set(null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.saveError'));
    }
  }

  async saveLoggingSettings(logging: LoggingSettings): Promise<void> {
    try {
      const payload: LoggingSettingsUpdateRequestBackend = {
        logging: { ...logging, path: './logs' },
      };
      await firstValueFrom(this.http.put<void>(`${this.apiUrl}logging`, payload));
      this.loggingState.set(payload.logging);
      this.loadErrorState.set(null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.logging.saveError'));
    }
  }

  // The instance payload is validated per kind on the backend; Seerr connections are
  // identified without an instance name, so the name is optional here.
  async testConnection(
    kind: ArrApplicationKind,
    instance: { id: string; name?: string; baseUrl: string; apiKey: string },
  ): Promise<TestArrInstanceConnectionResult> {
    try {
      return await firstValueFrom(
        this.http.post<TestArrInstanceConnectionResult>(`${this.apiUrl}test-connection`, {
          kind,
          instance: instance,
        }),
      );
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.connection.testError'));
    }
  }

  isSyncing(kind: ArrApplicationKind, instanceId: string): boolean {
    return this.syncingArrInstanceKeys().has(AppSettingsService.syncingKey(kind, instanceId));
  }

  async syncInstance(kind: ArrApplicationKind, instanceId: string): Promise<SyncArrInstanceResult> {
    const key: string = AppSettingsService.syncingKey(kind, instanceId);
    this.syncingArrInstanceKeys.update((keys: ReadonlySet<string>): ReadonlySet<string> => new Set<string>([...keys, key]));
    try {
      const result: SyncInstanceResponseDto = await firstValueFrom(
        this.http.post<SyncInstanceResponseDto>(`${this.apiUrl}sync`, {
          kind,
          instanceId,
        }),
      );

      return {
        message: result.message ?? '',
        syncedAt: result.syncedAt ?? null,
        itemCount: result.itemCount ?? 0,
      };
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.sync.error'));
    } finally {
      this.syncingArrInstanceKeys.update((keys: ReadonlySet<string>): ReadonlySet<string> => {
        const remaining: Set<string> = new Set<string>(keys);
        remaining.delete(key);
        return remaining;
      });
    }
  }

  private static syncingKey(kind: ArrApplicationKind, instanceId: string): string {
    return `${kind}:${instanceId}`;
  }

  private mapHttpError(error: unknown, fallbackMessage: string): Error {
    if (error instanceof HttpErrorResponse) {
      const details: { message: string | null; fieldErrors: Record<string, string[]> } | null = this.readProblemDetail(error.error);

      if (details !== null) {
        const message: string = details.message ?? error.message ?? fallbackMessage;
        if (Object.keys(details.fieldErrors).length > 0) {
          return new ValidationProblemError(message, details.fieldErrors);
        }

        return new Error(message);
      }

      return new Error(error.message ?? fallbackMessage);
    }

    return new Error(fallbackMessage);
  }

  private readProblemDetail(errorBody: unknown): { message: string | null; fieldErrors: Record<string, string[]> } | null {
    if (!errorBody || typeof errorBody !== 'object') {
      return null;
    }

    const errorObject: Record<string, unknown> = errorBody as Record<string, unknown>;
    const fieldErrors: Record<string, string[]> = this.readFieldErrors(errorObject['errors']);
    const firstFieldMessage: string | null = this.readFirstFieldMessage(fieldErrors);
    const detailValue: string | null = this.readStringProperty(errorObject, 'detail');
    const titleValue: string | null = this.readStringProperty(errorObject, 'title');

    if (firstFieldMessage !== null) {
      return { message: firstFieldMessage, fieldErrors };
    }

    if (detailValue !== null) {
      return { message: detailValue, fieldErrors };
    }

    if (titleValue !== null) {
      return { message: titleValue, fieldErrors };
    }

    return null;
  }

  private readFieldErrors(errorValue: unknown): Record<string, string[]> {
    const fieldErrors: Record<string, string[]> = {};
    if (!errorValue || typeof errorValue !== 'object') {
      return fieldErrors;
    }

    for (const [key, value] of Object.entries(errorValue as Record<string, unknown>)) {
      const messages: string[] = this.readMessages(value);
      if (messages.length > 0) {
        fieldErrors[key] = messages;
      }
    }

    return fieldErrors;
  }

  private readMessages(value: unknown): string[] {
    if (!Array.isArray(value)) {
      return [];
    }

    return value.filter((item): item is string => typeof item === 'string' && item.trim().length > 0);
  }

  private readFirstFieldMessage(fieldErrors: Record<string, string[]>): string | null {
    for (const messages of Object.values(fieldErrors)) {
      const message: string | undefined = messages.find((value) => value.trim().length > 0);
      if (message !== undefined) {
        return message;
      }
    }

    return null;
  }

  private readStringProperty(errorObject: Record<string, unknown>, propertyName: 'detail' | 'title'): string | null {
    const value: unknown = errorObject[propertyName];
    if (typeof value !== 'string') {
      return null;
    }

    const trimmedValue: string = value.trim();
    return trimmedValue.length > 0 ? trimmedValue : null;
  }

  private async loadBaseSettingsInternalAsync(): Promise<void> {
    try {
      const backendSettings: GeneralSettingsBackend = await firstValueFrom(this.http.get<GeneralSettingsBackend>(`${this.apiUrl}general`));

      const language: AppLanguage = BACKEND_LANGUAGE_TO_APP_LANGUAGE[backendSettings.language];
      const theme: AppTheme = BACKEND_THEME_TO_APP_THEME[backendSettings.theme];

      this.applyLanguage(language);
      this.applyTheme(theme);
      this.languageState.set(language);
      this.themeState.set(theme);
      this.titleSortingState.set({
        enabled: backendSettings.titleSorting?.enabled ?? false,
        ignoredLeadingWords: [...(backendSettings.titleSorting?.ignoredLeadingWords ?? [])],
      });
      this.loadErrorState.set(null);
    } catch (error) {
      this.applyLanguage(DEFAULT_LANGUAGE);
      this.applyTheme(DEFAULT_THEME);
      this.languageState.set(DEFAULT_LANGUAGE);
      this.themeState.set(DEFAULT_THEME);
      this.loadErrorState.set(this.mapHttpError(error, this.i18n.t('settings.loadError')).message);
    } finally {
      this.loadedState.set(true);
      this.loadingPromise = undefined;
    }
  }

  private async saveInstanceScopedAsync(scope: ArrApplicationKind, index: number, instance: ArrInstanceSettings): Promise<void> {
    const endpointPath: string = `${this.apiUrl}${scope}/${index}`;

    try {
      await firstValueFrom(this.http.put<void>(endpointPath, instance));
      const nextInstances: ArrInstanceSettings[] = [
        ...(scope === ARR_APPLICATION_KINDS.SONARR ? this.sonarrInstancesState() : this.radarrInstancesState()),
      ];
      if (index >= 0 && index < nextInstances.length) {
        nextInstances[index] = instance;
      } else {
        nextInstances.push(instance);
      }

      if (scope === ARR_APPLICATION_KINDS.SONARR) {
        this.sonarrInstancesState.set(nextInstances);
      } else {
        this.radarrInstancesState.set(nextInstances);
      }
      this.loadErrorState.set(null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.saveError'));
    }
  }

  private async deleteInstanceScopedAsync(scope: ArrApplicationKind, index: number): Promise<void> {
    const endpointPath: string = `${this.apiUrl}${scope}/${index}`;

    try {
      await firstValueFrom(this.http.delete<void>(endpointPath));
      if (scope === ARR_APPLICATION_KINDS.SONARR) {
        this.sonarrInstancesState.update((current) => current.filter((_, currentIndex) => currentIndex !== index));
      } else {
        this.radarrInstancesState.update((current) => current.filter((_, currentIndex) => currentIndex !== index));
      }
      this.loadErrorState.set(null);
    } catch (error) {
      throw this.mapHttpError(error, this.i18n.t('settings.saveError'));
    }
  }

  private applyLanguage(language: AppLanguage): void {
    this.i18n.setLanguage(language);
    this.document.documentElement.lang = language;

    switch (language) {
      case 'de':
        registerLocaleData(localeDe);
        break;
      case 'fr':
        registerLocaleData(localeFr);
        break;
      case 'en':
        registerLocaleData(localeEn);
        break;
    }
  }

  private applyTheme(theme: AppTheme): void {
    this.document.documentElement.dataset['theme'] = this.resolveTheme(theme);
  }

  private resolveTheme(theme: AppTheme): 'light' | 'dark' {
    if (theme !== 'system') {
      return theme;
    }

    return typeof globalThis.matchMedia === 'function' && globalThis.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
}
