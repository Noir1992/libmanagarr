import eslint from '@eslint/js';
import { defineConfig } from 'eslint/config';
import globals from 'globals';
import playwright from 'eslint-plugin-playwright';
import tseslint from 'typescript-eslint';

export default defineConfig(
  { ignores: ['node_modules/**', 'artifacts/**', 'seeds/**'] },
  {
    files: ['**/*.mjs', '**/*.ts'],
    extends: [eslint.configs.recommended],
    languageOptions: { globals: globals.node },
    rules: {
      'no-nested-ternary': 'error',
    },
  },
  {
    files: ['**/*.ts'],
    extends: [tseslint.configs.recommended],
    languageOptions: {
      globals: globals.browser,
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-misused-promises': 'error',
    },
  },
  {
    files: ['specs/**/*.ts'],
    extends: [playwright.configs['flat/recommended']],
  },
);
