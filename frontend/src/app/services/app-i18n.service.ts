import { Injectable, signal, WritableSignal } from '@angular/core';
import { AppLanguage } from '../interfaces/general/app-language';
import { i18nDe } from './i18n/_i18n-de';
import { i18nEn } from './i18n/_i18n-en';
import { i18nFr } from './i18n/_i18n-fr';
import { TranslationKey } from './i18n/_i18n-key';

const TRANSLATIONS: Record<AppLanguage, Record<TranslationKey, string>> = {
  en: i18nEn,
  de: i18nDe,
  fr: i18nFr,
};

@Injectable({ providedIn: 'root' })
export class AppI18nService {
  private readonly language: WritableSignal<AppLanguage> = signal<AppLanguage>('en');

  setLanguage(language: AppLanguage): void {
    this.language.set(language);
  }

  t(key: TranslationKey, replace?: Record<string, string>): string {
    let result: string = TRANSLATIONS[this.language()][key];

    if (replace) {
      Object.keys(replace).forEach((replaceKey) => {
        result = result.replace('{' + replaceKey + '}', replace[replaceKey]);
      });
    }

    return result;
  }

  tCount(count: number, singularKey: TranslationKey, pluralKey: TranslationKey, replace?: Record<string, string>): string {
    const key: TranslationKey = count === 1 ? singularKey : pluralKey;
    return this.t(key, replace);
  }

  tForLanguage(language: AppLanguage, key: TranslationKey): string {
    return TRANSLATIONS[language][key];
  }
}
