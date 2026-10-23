import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { LibraryFilterValues } from '../interfaces/library/library-filter-values';
import { LibraryRequestFilter } from '../interfaces/library/library-request-filter';
import { SonarrLibraryBatchResponse } from '../interfaces/library/sonarr-library-batch-response';

@Injectable({ providedIn: 'root' })
export class SonarrApiService {
  private readonly http: HttpClient = inject(HttpClient);

  fetchLibrary(filters: LibraryRequestFilter): Observable<SonarrLibraryBatchResponse> {
    return this.http.post<SonarrLibraryBatchResponse>('/api/sonarr/instances/library', filters);
  }

  fetchFilterValues(instanceIds: string[]): Observable<LibraryFilterValues> {
    return this.http.post<LibraryFilterValues>('/api/sonarr/instances/library/filter-values', { instanceIds });
  }
}
