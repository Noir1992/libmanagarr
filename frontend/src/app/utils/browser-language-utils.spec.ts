import { detectBrowserLanguage } from './browser-language-utils';

describe('browser language', () => {
  for (const [language, expected] of [
    ['de-DE', 'de'],
    ['de-AT', 'de'],
    ['fr-CA', 'fr'],
    ['FR-fr', 'fr'],
    ['en-GB', 'en'],
    ['es-ES', 'en'],
    ['', 'en'],
  ]) {
    it(`uses ${expected} for ${language || 'an absent language'}`, () => {
      expect(detectBrowserLanguage(language)).toBe(expected);
    });
  }
});
