import { TitleSortingSettings } from './title-sorting-settings';

export interface GeneralSettingsBackend {
  language: 'En' | 'De' | 'Fr';
  theme: 'Light' | 'Dark' | 'System';
  titleSorting?: TitleSortingSettings;
}
