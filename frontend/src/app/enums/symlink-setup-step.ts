export type SymlinkSetupStep =
  | 'LinkCreated'
  | 'MonitoringDisabled'
  | 'RescanRequested'
  | 'EpisodeAssigned'
  | 'MetadataMatched'
  | 'Renamed'
  | 'Completed';
