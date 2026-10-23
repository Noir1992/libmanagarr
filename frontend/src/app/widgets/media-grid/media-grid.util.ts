export interface GridWindowLayout {
  readonly itemCount: number;
  readonly columns: number;
  readonly cardHeight: number;
  readonly rowGap: number;
  readonly gridTop: number;
  readonly scrollTop: number;
  readonly viewportHeight: number;
  readonly overscanRows: number;
}

export interface GridWindowRange {
  readonly firstRow: number;
  readonly lastRow: number;
}

export function countGridTemplateColumns(gridTemplateColumns: string): number {
  const tracks: string[] = gridTemplateColumns.split(' ').filter((track: string): boolean => track.trim().length > 0);

  if (tracks.length === 0) {
    return 0;
  }

  const resolved: boolean = tracks.every((track: string): boolean => {
    const width: number = Number.parseFloat(track);
    return Number.isFinite(width) && width > 0;
  });

  return resolved ? tracks.length : 0;
}

export function computeGridWindow(layout: GridWindowLayout): GridWindowRange | null {
  const itemCount: number = layout.itemCount;
  const columns: number = layout.columns;

  if (!Number.isInteger(itemCount) || itemCount <= 0 || !Number.isInteger(columns) || columns <= 0) {
    return null;
  }

  if (layout.cardHeight <= 0 || layout.rowGap < 0 || layout.viewportHeight <= 0 || layout.overscanRows < 0) {
    return null;
  }

  if (!Number.isFinite(layout.gridTop) || !Number.isFinite(layout.scrollTop)) {
    return null;
  }

  const rowHeight: number = layout.cardHeight + layout.rowGap;
  const totalRows: number = Math.ceil(itemCount / columns);
  const visibleTop: number = layout.scrollTop - layout.gridTop;
  const visibleBottom: number = visibleTop + layout.viewportHeight;
  const overscan: number = Math.ceil(layout.overscanRows);

  const firstRow: number = Math.min(Math.max(Math.floor(visibleTop / rowHeight) - overscan, 0), totalRows - 1);
  const lastRow: number = Math.min(Math.max(Math.ceil(visibleBottom / rowHeight) - 1 + overscan, 0), totalRows - 1);

  if (firstRow <= 0 && lastRow >= totalRows - 1) {
    return null;
  }

  return { firstRow, lastRow: Math.max(firstRow, lastRow) };
}
