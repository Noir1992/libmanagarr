import { LibrarySortDirection } from '../../enums/library-sort-direction';
import { LibrarySortField } from '../../enums/library-sort-field';

export interface LibraryRequestFilter {
  excludedFilters?: string[];
  airedFilter?: string;
  monitoredFilter?: string;
  upgradeTargetFilter?: string;
  fileStatusFilter?: string;
  episodeMonitoredFilter?: string;
  types?: string[];
  statuses?: string[];
  genres?: string[];
  tags?: string[];
  profiles?: string[];
  audioLanguages?: string[];
  subtitleLanguages?: string[];
  resolutions?: string[];
  videoCodecs?: string[];
  audioCodecs?: string[];
  dynamicRanges?: string[];
  containerFormats?: string[];
  videoBitrateMin?: number | null;
  videoBitrateMax?: number | null;
  scoreMin?: number | null;
  scoreMax?: number | null;
  addedFrom?: string | null;
  addedTo?: string | null;
  airedFrom?: string | null;
  airedTo?: string | null;
  radarrInstanceFilter: string[];
  sonarrInstanceFilter: string[];
  sortField: LibrarySortField;
  sortDirection: LibrarySortDirection;
}
