export type LibraryTrendDateRange = Readonly<{
  startDate: string;
  endDate: string;
}>;

export function buildDefaultHistoryDateRange(): LibraryTrendDateRange {
  const endDate: Date = new Date();
  const startDate: Date = new Date(endDate);
  startDate.setMonth(startDate.getMonth() - 1);

  return {
    startDate: formatDateValue(startDate),
    endDate: formatDateValue(endDate),
  };
}

function formatDateValue(value: Date): string {
  const year: number = value.getUTCFullYear();
  const month: string = `${value.getUTCMonth() + 1}`.padStart(2, '0');
  const day: string = `${value.getUTCDate()}`.padStart(2, '0');
  return `${year}-${month}-${day}`;
}
