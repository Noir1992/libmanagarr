export function convertDateAndTimeToDateTime(date: string, time: string): Date | null {
  let value: Date | null = null;

  if (date) {
    value = new Date(date);

    if (time) {
      const timeSplit: string[] = time.split(':');
      value.setHours(+timeSplit[0]);
      value.setMinutes(+timeSplit[1]);
    }
  }

  return value;
}
