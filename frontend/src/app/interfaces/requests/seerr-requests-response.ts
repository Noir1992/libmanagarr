import { SeerrRequest } from './seerr-request';

export interface SeerrRequestsResponse {
  requests: SeerrRequest[];
  total: number;
  page: number;
  limit: number;
}
