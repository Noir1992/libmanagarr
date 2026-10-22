import { computeGridWindow, countGridTemplateColumns } from './media-grid.util';

describe('countGridTemplateColumns', () => {
  it('counts resolved column tracks', () => {
    expect(countGridTemplateColumns('175px 175px 175px')).toBe(3);
    expect(countGridTemplateColumns('185.5px 185.5px 185.5px 185.5px')).toBe(4);
  });

  it('returns 0 when the template is not resolved to concrete tracks', () => {
    expect(countGridTemplateColumns('none')).toBe(0);
    expect(countGridTemplateColumns('repeat(auto-fill, minmax(175px, 1fr))')).toBe(0);
    expect(countGridTemplateColumns('')).toBe(0);
  });
});

describe('computeGridWindow', () => {
  const layout = {
    itemCount: 100,
    columns: 5,
    cardHeight: 300,
    rowGap: 20,
    gridTop: 0,
    scrollTop: 0,
    viewportHeight: 640,
    overscanRows: 2,
  };

  it('returns null when the whole grid fits into the window', () => {
    expect(computeGridWindow({ ...layout, itemCount: 15 })).toBeNull();
  });

  it('returns null for layouts that cannot be measured', () => {
    expect(computeGridWindow({ ...layout, itemCount: 0 })).toBeNull();
    expect(computeGridWindow({ ...layout, itemCount: 10.5 })).toBeNull();
    expect(computeGridWindow({ ...layout, columns: 0 })).toBeNull();
    expect(computeGridWindow({ ...layout, cardHeight: 0 })).toBeNull();
    expect(computeGridWindow({ ...layout, rowGap: -1 })).toBeNull();
    expect(computeGridWindow({ ...layout, viewportHeight: 0 })).toBeNull();
    expect(computeGridWindow({ ...layout, overscanRows: -1 })).toBeNull();
    expect(computeGridWindow({ ...layout, gridTop: Number.NaN })).toBeNull();
    expect(computeGridWindow({ ...layout, scrollTop: Number.NaN })).toBeNull();
  });

  it('windows the top of a long grid with overscan rows', () => {
    expect(computeGridWindow(layout)).toEqual({ firstRow: 0, lastRow: 3 });
  });

  it('tracks the window while scrolling', () => {
    expect(computeGridWindow({ ...layout, scrollTop: 3200 })).toEqual({ firstRow: 8, lastRow: 13 });
  });

  it('clamps the window at the bottom of the grid', () => {
    expect(computeGridWindow({ ...layout, scrollTop: 6000 })).toEqual({ firstRow: 16, lastRow: 19 });
  });

  it('renders the first rows only while the grid is still below the viewport', () => {
    expect(computeGridWindow({ ...layout, gridTop: 1200 })).toEqual({ firstRow: 0, lastRow: 0 });
  });
});
