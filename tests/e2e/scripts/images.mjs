import { setTimeout as delay } from 'node:timers/promises';

const transientPullFailure =
  /retry-after|rate.?limit|too.?many.?requests|\b429\b|connection reset|i\/o timeout|TLS handshake timeout|unexpected EOF|\b50[0234]\b/i;

// Pull pinned external images sequentially before Compose starts services. A registry outage
// must not retry browser tests, baseline restores, or other operations that can change state.
export function createServiceImagePuller({ images, execute, signal, wait = delay, log = console.warn }) {
  const available = new Set();
  return async (services) => {
    for (const service of new Set(services)) {
      const image = images[service];
      if (!image || available.has(image)) continue;
      signal?.throwIfAborted();
      try {
        await execute(['image', 'inspect', image], { capture: true });
        available.add(image);
        continue;
      } catch (error) {
        if (!/no such (?:image|object)/i.test(error.output ?? error.message)) throw error;
      }
      for (let attempt = 0; ; attempt++) {
        signal?.throwIfAborted();
        try {
          await execute(['pull', image], { timeout: 180_000 });
          available.add(image);
          break;
        } catch (error) {
          if (attempt >= 3 || !transientPullFailure.test(error.output ?? error.message)) throw error;
          const milliseconds = 2_000 * 2 ** attempt;
          log(`Temporary image download failure for ${service}; retry ${attempt + 1}/3 in ${milliseconds / 1_000}s.`);
          await wait(milliseconds, undefined, { signal });
        }
      }
    }
  };
}
