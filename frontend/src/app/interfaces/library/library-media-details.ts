export interface LibraryMediaDetails {
  resolution?: string | null;
  originalResolution?: string | null;
  videoCodec?: string | null;
  audioCodec?: string | null;
  dynamicRange?: string | null;
  containerFormat?: string | null;
  videoBitrate?: number | null;
  audioLanguages?: string[];
  subtitleLanguages?: string[];
  size?: number | null;
  durationSeconds?: number | null;
  releaseGroup?: string | null;
  relativePath?: string | null;
  createdAt?: string | null;
}
