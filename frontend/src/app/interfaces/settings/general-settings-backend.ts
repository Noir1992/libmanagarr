import { SizeUnitSystem } from '../general/size-unit-system';
import { AuthProtectionSettings } from './auth-protection-settings';
import { TitleSortingSettings } from './title-sorting-settings';

export interface GeneralSettingsBackend {
  language: 'En' | 'De' | 'Fr';
  sizeUnitSystem?: SizeUnitSystem;
  theme: 'Light' | 'Dark' | 'System';
  titleSorting?: TitleSortingSettings;
  authProtection?: AuthProtectionSettings;
}
