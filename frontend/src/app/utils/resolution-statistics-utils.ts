import { CodecStatisticItem } from '../interfaces/codec-statistic/codec-statistic-item';

export const RESOLUTION_GROUPS = ['4k', '1440p', '1080p', '720p', '576p', '480p', 'SD', 'unknown'] as const;
type ResolutionGroup = (typeof RESOLUTION_GROUPS)[number];

export function normalizeResolutionGroup(raw: string): ResolutionGroup {
  const value = raw.trim().toLowerCase();
  if (value === 'sd') return 'SD';
  if (['4k', 'uhd', '8k'].includes(value)) return '4k';

  const dimensions = /^(\d+)\s*[x×]\s*(\d+)$/.exec(value);
  const standard = /^(\d+)[pi]?$/.exec(value);
  let height: number;
  if (dimensions) {
    const width = Number(dimensions[1]);
    const actualHeight = Number(dimensions[2]);
    if (width <= 0 || actualHeight <= 0) return 'unknown';
    // Include width so widescreen films retain their category after cropping.
    height = Math.max(actualHeight, (width * 9) / 16);
  } else if (standard) {
    height = Number(standard[1]);
    if (height <= 0) return 'unknown';
  } else {
    return 'unknown';
  }

  if (height < 440) return 'SD';
  const standards = [480, 576, 720, 1080, 1440, 2160];
  const nearest = standards.reduce((best, candidate) => (Math.abs(candidate - height) < Math.abs(best - height) ? candidate : best));
  return nearest === 2160 ? '4k' : (`${nearest}p` as ResolutionGroup);
}

export function groupResolutionStatistics(items: readonly CodecStatisticItem[]): CodecStatisticItem[] {
  const groups = new Map<ResolutionGroup, CodecStatisticItem>();
  const total = items.reduce((count, item) => count + item.count, 0);
  for (const item of items) {
    const label = normalizeResolutionGroup(item.label);
    let group = groups.get(label);
    if (!group) {
      group = { label, count: 0, percentage: 0, totalSizeBytes: 0, mediaFiles: [] };
      groups.set(label, group);
    }
    group.count += item.count;
    group.totalSizeBytes += item.totalSizeBytes;
    group.mediaFiles.push(...item.mediaFiles);
  }
  return RESOLUTION_GROUPS.flatMap((label) => {
    const group = groups.get(label);
    return group ? [{ ...group, percentage: total === 0 ? 0 : Math.round((group.count * 10000) / total) / 100 }] : [];
  }).sort((left, right) => right.count - left.count);
}
