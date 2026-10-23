import { MediaDetailData } from '../interfaces/general/media-detail-data';
import { RadarrLibraryMovieTable } from '../interfaces/library/radarr-library-movie-table';
import { SonarrLibraryEpisodeTable } from '../interfaces/library/sonarr-library-episode-table';
import { SonarrLibrarySeriesTable } from '../interfaces/library/sonarr-library-series-table';

export function movieDetailData(movie: RadarrLibraryMovieTable, instanceName: string): MediaDetailData {
  return { ...movie, instanceName };
}

export function episodeDetailData(
  episode: SonarrLibraryEpisodeTable,
  seasonNumber: number,
  series: SonarrLibrarySeriesTable,
  instanceName: string,
): MediaDetailData {
  return {
    ...episode,
    url: series.url,
    instanceName,
    seriesTitle: series.title,
    seasonNumber,
    episodeNumber: episode.number,
    upgradeUntilCustomFormatScore: series.upgradeUntilCustomFormatScore,
  };
}

export function isDetailRowActivation(event: Event): boolean {
  if (event instanceof KeyboardEvent) {
    if (event.target !== event.currentTarget) return false;
    event.preventDefault();
    return true;
  }
  return !(event.target instanceof Element && event.target.closest('a, button, input, select, textarea'));
}

export function specialsLast<T extends { number: number }>(seasons: readonly T[]): T[] {
  return [...seasons.filter((season) => season.number !== 0), ...seasons.filter((season) => season.number === 0)];
}
