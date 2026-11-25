import eslint from '@eslint/js';
import { defineConfig } from 'eslint/config';
import tseslint from 'typescript-eslint';
import vitest from '@vitest/eslint-plugin';
import angular from 'angular-eslint';

export default defineConfig(
  {
    ignores: ['.angular/**', 'dist/**', 'coverage/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [eslint.configs.recommended, tseslint.configs.recommended, angular.configs.tsRecommended],
    processor: angular.processInlineTemplates,
    rules: {
      'no-nested-ternary': 'error',
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended],
  },
  {
    files: ['**/*.spec.ts'],
    plugins: { vitest },
    rules: {
      ...vitest.configs.recommended.rules,
      // Vitest takes the assertion message as a second argument, which the specs migrated from
      // jasmine.withContext rely on.
      'vitest/valid-expect': ['error', { maxArgs: 2 }],
    },
  },
);
