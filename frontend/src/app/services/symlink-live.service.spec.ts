import { TestBed } from '@angular/core/testing';
import type { MockInstance } from 'vitest';
import { SymlinkLiveService } from './symlink-live.service';
import { SymlinkService } from './symlink.service';

class Stream {
  static instances: Stream[] = [];
  onmessage: ((message: { data: string }) => void) | null = null;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  close = vi.fn();
  constructor(public url: string) {
    Stream.instances.push(this);
  }
  send(areas: string[], revision = 1, generation = 'server-1'): void {
    this.onmessage?.({ data: JSON.stringify({ areas, revision, generation }) });
  }
}

describe('SymlinkLiveService', () => {
  let live: SymlinkLiveService;
  let original: typeof EventSource;
  let hidden: boolean;
  let symlinks: Record<'invalidateReads' | 'refreshAreas' | 'refreshCount' | 'isAreaCurrent', MockInstance>;

  beforeEach(() => {
    vi.useFakeTimers();
    original = globalThis.EventSource;
    globalThis.EventSource = Stream as unknown as typeof EventSource;
    Stream.instances = [];
    hidden = false;
    vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
    symlinks = {
      invalidateReads: vi.fn<SymlinkService['invalidateReads']>(),
      refreshAreas: vi.fn<SymlinkService['refreshAreas']>(),
      refreshCount: vi.fn<SymlinkService['refreshCount']>(),
      isAreaCurrent: vi.fn<SymlinkService['isAreaCurrent']>(),
    };
    symlinks.isAreaCurrent.mockReturnValue(false);
    symlinks.refreshAreas.mockResolvedValue(undefined);
    symlinks.refreshCount.mockResolvedValue(undefined);
    TestBed.configureTestingModule({ providers: [{ provide: SymlinkService, useValue: symlinks }] });
    live = TestBed.inject(SymlinkLiveService);
  });

  afterEach(() => {
    live.stop();
    globalThis.EventSource = original;
    vi.useRealTimers();
  });

  it('opens one stream, reads only the count outside the page and closes at logout', async () => {
    live.start();
    live.start();
    expect(Stream.instances.length).toBe(1);
    Stream.instances[0].onopen?.();
    await advance(150);
    expect(symlinks.refreshCount).toHaveBeenCalledTimes(1);
    expect(symlinks.refreshAreas).not.toHaveBeenCalled();
    await advance(65000);
    expect(symlinks.refreshCount).toHaveBeenCalledTimes(1);
    live.stop();
    expect(Stream.instances[0].close).toHaveBeenCalled();
  });

  it('coalesces changes and performs another read for events arriving during a read', async () => {
    let finish!: () => void;
    symlinks.refreshAreas.mockImplementation(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
    live.start();
    live.activatePage();
    const stream = Stream.instances[0];
    stream.send(['links']);
    await advance(150);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(1);
    stream.send(['rejections'], 2);
    stream.send(['suggestions', 'count'], 3);
    finish();
    await advance(0);
    symlinks.refreshAreas.mockResolvedValue(undefined);
    await advance(150);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(2);
    expect(symlinks.refreshAreas.mock.lastCall![0].has('rejections')).toBe(true);
    expect(symlinks.refreshAreas.mock.lastCall![0].has('suggestions')).toBe(true);
  });

  it('defers background-tab reads and reconciles once on return', async () => {
    live.start();
    live.activatePage();
    Stream.instances[0].send(['links']);
    await advance(150);
    symlinks.refreshAreas.mockClear();
    hidden = true;
    Stream.instances[0].send(['links'], 2);
    Stream.instances[0].send(['suggestions'], 3);
    await advance(60000);
    expect(symlinks.refreshAreas).not.toHaveBeenCalled();
    hidden = false;
    document.dispatchEvent(new Event('visibilitychange'));
    await advance(150);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(1);
  });

  it('reconnects with bounded backoff and resyncs even at an unchanged revision', async () => {
    live.start();
    Stream.instances[0].send(['count'], 100);
    await advance(150);
    Stream.instances[0].onerror?.();
    await advance(999);
    expect(Stream.instances.length).toBe(1);
    await advance(1);
    Stream.instances[1].onopen?.();
    Stream.instances[1].send(['count'], 0, 'server-2');
    await advance(150);
    expect(symlinks.refreshCount).toHaveBeenCalledTimes(2);
    live.stop();
    await advance(60000);
    expect(Stream.instances.length).toBe(2);
  });

  it('skips events already covered by an HTTP response and still resyncs on reconnect', async () => {
    live.start();
    Stream.instances[0].send(['count']);
    await advance(150);
    symlinks.refreshCount.mockClear();
    symlinks.isAreaCurrent.mockReturnValue(true);
    Stream.instances[0].send(['suggestions', 'count'], 2);
    await advance(150);
    expect(symlinks.refreshCount).not.toHaveBeenCalled();
    Stream.instances[0].onopen?.();
    await advance(150);
    expect(symlinks.refreshCount).toHaveBeenCalledTimes(1);
  });

  it('retries failed reads and stops list updates when leaving the page', async () => {
    live.start();
    live.activatePage();
    symlinks.refreshAreas.mockRejectedValue(new Error('temporary'));
    await advance(150);
    symlinks.refreshAreas.mockResolvedValue(undefined);
    await advance(1000);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(2);
    live.deactivatePage();
    Stream.instances[0].send(['suggestions', 'count']);
    await advance(150);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(2);
  });
});

async function advance(milliseconds: number): Promise<void> {
  vi.advanceTimersByTime(milliseconds);
  for (let index = 0; index < 10; index++) await Promise.resolve();
}
