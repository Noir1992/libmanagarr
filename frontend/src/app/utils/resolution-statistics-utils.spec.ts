import { CodecStatisticItem } from '../interfaces/codec-statistic/codec-statistic-item';
import { groupResolutionStatistics } from './resolution-statistics-utils';

function item(label: string, count = 1): CodecStatisticItem {
  return { label, count, percentage: 0, totalSizeBytes: count * 100, mediaFiles: [] };
}

describe('Resolution statistics groups', () => {
  const examples: Record<string, string[]> = {
    '4k': ['3840x2160', '2160p', '4K', 'UHD', '7680x4320'],
    '1440p': ['2560x1440', '1440p'],
    '1080p': [
      '1920x1080',
      '1440x1080',
      '1424x1068',
      '1432x1072',
      '1428x1068',
      '1460x1078',
      '1916x1080',
      '1280x960',
      '1920x810',
      '1920x960',
      '1920x1040',
      '1920x816',
      '1918x1080',
      '1920x1036',
      '1920x800',
      '1438x1080',
      '1448x1080',
      '1920x804',
      '1920x1048',
      '1920x808',
      '1912x1080',
      '1816x1080',
      '1888x1080',
      '1898x804',
      '1914x1080',
      '1914x816',
      '1918x1036',
      '1920x802',
      '1920x820',
      '1080i',
    ],
    '720p': ['1280x720', '720p'],
    '576p': ['736x544', '720x540', '720x576', '768x576'],
    '480p': ['608x448', '640x480', '848x480', '672x496'],
    SD: ['640x352', '576x432', '640x272', 'SD'],
    unknown: ['Unknown', '', 'N/A', 'garbage', '0x1080', '1920x0'],
  };
  for (const [group, resolutions] of Object.entries(examples)) {
    it(`assigns the supplied resolutions to ${group}`, () => {
      for (const resolution of resolutions) {
        expect(groupResolutionStatistics([item(resolution)])[0].label)
          .withContext(resolution)
          .toBe(group);
      }
    });
  }

  it('orders resolution groups by descending percentage', () => {
    const groups = groupResolutionStatistics(
      ['Unknown', '640x352', '640x480', '720x576', '1280x720', '1920x1080', '2560x1440', '3840x2160'].map((label, index) =>
        item(label, 100 - index),
      ),
    );
    expect(groups.map((group) => group.label)).toEqual(['unknown', 'SD', '480p', '576p', '720p', '1080p', '1440p', '4k']);
  });

  it('merges counts, sizes and files and recomputes percentages without changing raw statistics', () => {
    const full = item('1920x1080', 3);
    const cropped = item('1920x800', 1);
    const file = {
      title: 'Film',
      source: 'Radarr',
      instanceName: 'Radarr',
      titleSlug: 'film',
      sizeBytes: 100,
      relativePath: 'Film.mkv',
      rootTitle: 'Film',
    };
    cropped.mediaFiles = [file];
    const groups = groupResolutionStatistics([item('3840x2160', 1), full, cropped]);
    expect(groups.map((group) => [group.label, group.count, group.percentage, group.totalSizeBytes])).toEqual([
      ['1080p', 4, 80, 400],
      ['4k', 1, 20, 100],
    ]);
    expect(groups[0].mediaFiles).toEqual([file]);
    expect(full.count).toBe(3);
    expect(full.mediaFiles).toEqual([]);
  });

  it('returns no categories for empty data', () => {
    expect(groupResolutionStatistics([])).toEqual([]);
  });
});
