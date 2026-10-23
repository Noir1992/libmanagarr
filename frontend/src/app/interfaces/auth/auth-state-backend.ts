import { GeneralSettingsBackend } from '../settings/general-settings-backend';

export interface AuthStateBackend {
  setupRequired: boolean;
  authenticated: boolean;
  language?: GeneralSettingsBackend['language'] | null;
}
