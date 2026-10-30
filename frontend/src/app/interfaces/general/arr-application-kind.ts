export const ARR_APPLICATION_KINDS: Readonly<{ SONARR: 'Sonarr'; RADARR: 'Radarr'; SEERR: 'Seerr' }> = {
  SONARR: 'Sonarr',
  RADARR: 'Radarr',
  SEERR: 'Seerr',
};

export type ArrApplicationKind = (typeof ARR_APPLICATION_KINDS)[keyof typeof ARR_APPLICATION_KINDS];
