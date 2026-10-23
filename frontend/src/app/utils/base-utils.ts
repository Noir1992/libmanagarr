export function normalizeBaseUrl(baseUrl: string): string {
  return baseUrl.replace(/\/+$/, '');
}

export function hashColor(str: string): string {
  let hash: number = 0;
  for (let i: number = 0; i < str.length; i++) {
    hash = Math.trunc(hash * 31 + str.codePointAt(i)!);
  }
  const h: number = Math.abs(hash) % 360;
  return `hsl(${h}, 45%, 28%)`;
}
