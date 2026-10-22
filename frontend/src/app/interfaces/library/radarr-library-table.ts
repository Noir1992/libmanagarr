import { RadarrLibraryMovieTable } from './radarr-library-movie-table';

export interface RadarrLibraryTable {
  instanceName: string;
  instanceId: string;
  syncedAt: string | null;
  movies: RadarrLibraryMovieTable[];
}
