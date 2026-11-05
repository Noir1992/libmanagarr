import { AuthProtectionSettings } from './auth-protection-settings';
import { TitleSortingSettings } from './title-sorting-settings';

export interface GeneralSettingsBackend {
  language: 'En' | 'De' | 'Fr';
  theme: 'Light' | 'Dark' | 'System';
  titleSorting?: TitleSortingSettings;
  authProtection?: AuthProtectionSettings;
}
