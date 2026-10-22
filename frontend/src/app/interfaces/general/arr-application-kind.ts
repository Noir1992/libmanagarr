export const ARR_APPLICATION_KINDS: Readonly<{ SONARR: 'Sonarr'; RADARR: 'Radarr' }> = {
  SONARR: 'Sonarr',
  RADARR: 'Radarr',
};

export type ArrApplicationKind = (typeof ARR_APPLICATION_KINDS)[keyof typeof ARR_APPLICATION_KINDS];
