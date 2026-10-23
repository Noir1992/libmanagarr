import { ApplicationConfig, inject, isDevMode, LOCALE_ID, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { appRoutes } from './app.routes';
import { authSessionInterceptor } from './interceptors/auth-session.interceptor';
import { csrfTokenInterceptor } from './interceptors/csrf-token.interceptor';
import { AppI18nService } from './services/app-i18n.service';
import { AppSettingsService } from './services/app-settings.service';
import { AuthService } from './services/auth.service';
import { detectBrowserLanguage } from './utils/browser-language-utils';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withXhr(), withInterceptors([csrfTokenInterceptor, authSessionInterceptor])),
    provideRouter(appRoutes),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
    provideAppInitializer(async (): Promise<void> => {
      const authService: AuthService = inject(AuthService);
      const appSettingsService: AppSettingsService = inject(AppSettingsService);
      const appI18nService: AppI18nService = inject(AppI18nService);

      await authService.initializeAsync();

      if (authService.state() === 'authenticated') {
        await appSettingsService.load();
        return;
      }

      appSettingsService.applyDefaultTheme();
      appI18nService.setLanguage(authService.preferredLanguage() ?? detectBrowserLanguage());
    }),
    {
      provide: LOCALE_ID,
      useFactory: (): string => {
        const appSettingsService: AppSettingsService = inject(AppSettingsService);

        return appSettingsService.localeId();
      },
    },
  ],
};
