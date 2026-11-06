/** A declined pair, with the titles that made it recognisable in the first place. */
export interface SymlinkRejection {
  radarrInstanceId: string;
  radarrMovieId: number;
  radarrMovieTitle: string | null;
  sonarrInstanceId: string;
  sonarrSeriesId: number;
  sonarrSeriesTitle: string | null;
  sonarrEpisodeId: number;
  sonarrEpisodeTitle: string | null;
  detail: string | null;
  createdAt: string;
}

/** The pair a decision or a preview belongs to. */
export interface SpecialMoviePairRequest {
  radarrInstanceId: string;
  radarrMovieId: number;
  sonarrInstanceId: string;
  sonarrSeriesId: number;
  sonarrEpisodeId: number;
}

/** Which file of the movie the link should point at; left out, it is only chosen when there is exactly one. */
export interface PreviewSymlinkLinkRequest extends SpecialMoviePairRequest {
  radarrFileId: number | null;
}

/** The path configuration the shown preview belonged to; moved paths make that preview a different link. */
export interface ConfirmSymlinkSuggestionRequest {
  configRevision: number;
}

/** Why a pair was declined, shown again in the archive. */
export interface RejectSymlinkSuggestionRequest {
  detail: string | null;
}

/** Whether later upgrades of this source keep being followed, plus the row the write presents. */
export interface SetSymlinkFollowUpRequest {
  autoFollowUp: boolean;
  revision: number;
}
