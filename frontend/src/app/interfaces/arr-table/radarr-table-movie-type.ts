import { RadarrLibraryMovieTable } from '../library/radarr-library-movie-table';

export type RadarrTableMovieType = RadarrLibraryMovieTable;

export interface RadarrTableType {
  instanceName: string;
  instanceId: string;
  syncedAt: string | null;
  movies: RadarrTableMovieType[];
}
