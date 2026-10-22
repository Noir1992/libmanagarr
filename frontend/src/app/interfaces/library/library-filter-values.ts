export type LibraryFilterCounts = Record<string, Record<string, number>>;

export interface LibraryFilterValues {
  counts?: LibraryFilterCounts;
  resolutions?: string[];
  videoCodecs?: string[];
  audioCodecs?: string[];
  dynamicRanges?: string[];
  containerFormats?: string[];
  genres: string[];
  tags: string[];
  statuses: string[];
  qualityProfiles: string[];
  audioLanguages: string[];
  subtitleLanguages: string[];
}
