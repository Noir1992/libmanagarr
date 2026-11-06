import { setTimeout as delay } from 'node:timers/promises';
import { readFileSync } from 'node:fs';

export const apiKey = '0123456789abcdef0123456789abcdef';
export const accounts = JSON.parse(readFileSync(new URL('../fixtures/accounts.json', import.meta.url), 'utf8'));
export const credentials = { username: accounts.administrator.username, password: accounts.administrator.password };

export async function json(base, path, { method = 'GET', body, headers = {} } = {}) {
  const response = await fetch(`${base}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(30_000),
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${base}${path}: ${response.status} ${text.slice(0, 1000)}`);
  return text ? JSON.parse(text) : undefined;
}

export async function until(description, probe, timeout = 180_000) {
  const deadline = Date.now() + timeout;
  let last;
  while (Date.now() < deadline) {
    try {
      const result = await probe();
      if (result) return result;
    } catch (error) {
      last = error;
    }
    await delay(1000);
  }
  throw new Error(`Timed out: ${description}`, { cause: last });
}

export const arr = (service, path, options = {}) =>
  json(`http://${service}:${service === 'sonarr' ? 8989 : 7878}`, `/api/v3${path}`, {
    ...options,
    headers: { 'X-Api-Key': apiKey, ...options.headers },
  });
