import { TestBed } from '@angular/core/testing';
import { SymlinkLiveService } from './symlink-live.service';
import { SymlinkService } from './symlink.service';

class Stream {
  static instances: Stream[] = [];
  onmessage: ((message: { data: string }) => void) | null = null;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  close = jasmine.createSpy('close');
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
  let symlinks: jasmine.SpyObj<SymlinkService>;

  beforeEach(() => {
    jasmine.clock().install();
    original = globalThis.EventSource;
    globalThis.EventSource = Stream as unknown as typeof EventSource;
    Stream.instances = [];
    hidden = false;
    spyOnProperty(document, 'hidden', 'get').and.callFake(() => hidden);
    symlinks = jasmine.createSpyObj<SymlinkService>('symlinks', ['invalidateReads', 'refreshAreas', 'refreshCount', 'isAreaCurrent']);
    symlinks.isAreaCurrent.and.returnValue(false);
    symlinks.refreshAreas.and.resolveTo();
    symlinks.refreshCount.and.resolveTo();
    TestBed.configureTestingModule({ providers: [{ provide: SymlinkService, useValue: symlinks }] });
    live = TestBed.inject(SymlinkLiveService);
  });

  afterEach(() => {
    live.stop();
    globalThis.EventSource = original;
    jasmine.clock().uninstall();
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
    symlinks.refreshAreas.and.callFake(
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
    symlinks.refreshAreas.and.resolveTo();
    await advance(150);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(2);
    expect(symlinks.refreshAreas.calls.mostRecent().args[0].has('rejections')).toBeTrue();
    expect(symlinks.refreshAreas.calls.mostRecent().args[0].has('suggestions')).toBeTrue();
  });

  it('defers background-tab reads and reconciles once on return', async () => {
    live.start();
    live.activatePage();
    Stream.instances[0].send(['links']);
    await advance(150);
    symlinks.refreshAreas.calls.reset();
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
    symlinks.refreshCount.calls.reset();
    symlinks.isAreaCurrent.and.returnValue(true);
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
    symlinks.refreshAreas.and.rejectWith(new Error('temporary'));
    await advance(150);
    symlinks.refreshAreas.and.resolveTo();
    await advance(1000);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(2);
    live.deactivatePage();
    Stream.instances[0].send(['suggestions', 'count']);
    await advance(150);
    expect(symlinks.refreshAreas).toHaveBeenCalledTimes(2);
  });
});

async function advance(milliseconds: number): Promise<void> {
  jasmine.clock().tick(milliseconds);
  for (let index = 0; index < 10; index++) await Promise.resolve();
}
