import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { LibraryFilterValues } from '../interfaces/library/library-filter-values';
import { LibraryRequestFilter } from '../interfaces/library/library-request-filter';
import { RadarrLibraryBatchResponse } from '../interfaces/library/radarr-library-batch-response';

@Injectable({ providedIn: 'root' })
export class RadarrApiService {
  private readonly http: HttpClient = inject(HttpClient);

  fetchLibrary(filters: LibraryRequestFilter): Observable<RadarrLibraryBatchResponse> {
    return this.http.post<RadarrLibraryBatchResponse>('/api/radarr/instances/library', filters);
  }

  fetchFilterValues(instanceIds: string[]): Observable<LibraryFilterValues> {
    return this.http.post<LibraryFilterValues>('/api/radarr/instances/library/filter-values', { instanceIds });
  }
}
