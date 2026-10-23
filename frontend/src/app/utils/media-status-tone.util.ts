import { MediaGridDetailTone } from '../interfaces/general/media-grid-item';

export function airStatusTone(status: string | undefined): MediaGridDetailTone | undefined {
  switch (status?.toLowerCase()) {
    case 'released':
    case 'ended':
      return 'success';
    case 'continuing':
      return 'info';
    case 'announced':
    case 'incinemas':
    case 'physicalrelease':
    case 'digitalrelease':
    case 'upcoming':
    case 'tba':
      return 'warning';
    case 'deleted':
      return 'danger';
    default:
      return undefined;
  }
}

export function fileStatusTone(hasFile: boolean | undefined): MediaGridDetailTone | undefined {
  if (hasFile === undefined) return undefined;
  return hasFile ? 'success' : 'danger';
}
