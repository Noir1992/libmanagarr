import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { CodecStatisticsResponse } from '../interfaces/codec-statistic/codec-statistics-response';
import { LibraryRequestFilter } from '../interfaces/library/library-request-filter';

@Injectable({ providedIn: 'root' })
export class CodecStatisticsApiService {
  private readonly http: HttpClient = inject(HttpClient);

  fetchCodecStatistics(
    sonarrInstanceIds: readonly string[],
    radarrInstanceIds: readonly string[],
    filter: Partial<LibraryRequestFilter> = {},
  ): Observable<CodecStatisticsResponse> {
    return this.http
      .post<CodecStatisticsResponse>('/api/statistics/codecs', {
        ...filter,
        sonarrInstanceFilter: sonarrInstanceIds,
        radarrInstanceFilter: radarrInstanceIds,
      })
      .pipe(
        map((response) => {
          if (!response.files) return response;
          const files = response.files;
          const hydrate = (items: CodecStatisticsResponse['videoCodecs'] = []) =>
            items.map((item) => ({
              ...item,
              mediaFiles: item.fileIds ? item.fileIds.map((id) => files[id]) : item.mediaFiles,
            }));
          return {
            ...response,
            containerFormats: hydrate(response.containerFormats),
            resolutions: hydrate(response.resolutions),
            dynamicRanges: hydrate(response.dynamicRanges),
            videoCodecs: hydrate(response.videoCodecs),
            audioCodecs: hydrate(response.audioCodecs),
            videoBitrates: hydrate(response.videoBitrates),
            audioLanguages: hydrate(response.audioLanguages),
            subtitleLanguages: hydrate(response.subtitleLanguages),
          };
        }),
      );
  }
}
