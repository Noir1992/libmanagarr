import cases from '../../../../test-data/title-sorting-cases.json';
import { buildTitleSortKey, foldTitleWord, isTitleWhitespace, normalizeLeadingWords, trimTitleWhitespace } from './title-sort-key-utils';

describe('title sort keys shared with the backend', () => {
  for (const testCase of cases.keyCases) {
    it(testCase.name, () => {
      const words = [...testCase.words];
      expect(buildTitleSortKey(testCase.title, { enabled: true, ignoredLeadingWords: words })).toBe(testCase.expected);
      expect(buildTitleSortKey(testCase.title, { enabled: false, ignoredLeadingWords: words })).toBe(testCase.title);
      expect(words).toEqual(testCase.words);
    });
  }

  for (const code of cases.whitespaceCodePoints) {
    it(`uses the explicit Unicode whitespace U+${code.toString(16)}`, () => {
      const whitespace = String.fromCodePoint(code);
      expect(isTitleWhitespace(whitespace)).toBeTrue();
      expect(trimTitleWhitespace(`${whitespace}The${whitespace}`)).toBe('The');
      expect(buildTitleSortKey(`${whitespace}The${whitespace}Office`, { enabled: true, ignoredLeadingWords: ['the'] })).toBe('Office');
    });
  }

  for (const testCase of cases.foldCases) {
    it(`uses pinned simple case folding for ${testCase.input}`, () => {
      expect(foldTitleWord(testCase.input)).toBe(testCase.expected);
    });
  }

  for (const testCase of cases.normalizationCases) {
    it(`normalizes and deduplicates ${JSON.stringify(testCase.words)}`, () => {
      const words = [...testCase.words];
      expect(normalizeLeadingWords(words)).toEqual(testCase.expected);
      expect(words).toEqual(testCase.words);
    });
  }
});
