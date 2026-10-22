import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LogEntry } from '../interfaces/log-entry';
import { AppLogLevel } from '../interfaces/general/app-log-level';

@Injectable({ providedIn: 'root' })
export class AppLogsService {
  private readonly http: HttpClient = inject(HttpClient);
  private readonly apiUrl: string = '/api/logs/';

  async loadLogs(startDateTime: string, endDateTime: string, levels: readonly AppLogLevel[]): Promise<readonly LogEntry[]> {
    let params: HttpParams = new HttpParams().set('startDateTime', startDateTime).set('endDateTime', endDateTime);

    if (levels.length === 0) {
      params = params.set('levels', '');
    } else {
      for (const level of levels) {
        params = params.append('levels', level);
      }
    }

    return await firstValueFrom(this.http.get<LogEntry[]>(this.apiUrl, { params }));
  }
}
