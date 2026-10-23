import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { LibraryTrendStatisticsResponse } from '../interfaces/library-trend/library-trend-statistics-response';

@Injectable({ providedIn: 'root' })
export class LibraryTrendStatisticsApiService {
  private readonly http: HttpClient = inject(HttpClient);

  fetchLibraryTrendStatistics(
    sonarrInstanceIds: readonly string[],
    radarrInstanceIds: readonly string[],
    start?: string | null,
    end?: string | null,
  ): Observable<LibraryTrendStatisticsResponse> {
    let params: HttpParams = new HttpParams()
      .set('sonarrInstanceIds', sonarrInstanceIds.join(','))
      .set('radarrInstanceIds', radarrInstanceIds.join(','));

    if (typeof start === 'string') {
      params = params.set('start', start);
    }

    if (typeof end === 'string') {
      params = params.set('end', end);
    }

    return this.http.get<LibraryTrendStatisticsResponse>('/api/statistics/library-trend', {
      params,
    });
  }
}
