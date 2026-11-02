import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CalendarResponse } from '../interfaces/calendar/calendar-response';

@Injectable({ providedIn: 'root' })
export class CalendarApiService {
  private readonly http: HttpClient = inject(HttpClient);

  fetchCalendar(startDate: string, endDate: string, timeZone: string, refresh: boolean = false): Observable<CalendarResponse> {
    let params = new HttpParams().set('startDate', startDate).set('endDate', endDate).set('timeZone', timeZone);

    if (refresh) {
      params = params.set('refresh', 'true');
    }

    return this.http.get<CalendarResponse>('/api/calendar', { params });
  }
}
