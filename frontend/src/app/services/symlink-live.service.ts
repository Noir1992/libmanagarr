import { inject, Injectable } from '@angular/core';
import { SymlinkService } from './symlink.service';

const ALL = ['links', 'suggestions', 'rejections', 'count', 'settings'];
interface Change {
  generation: string;
  revision: number;
  areas: string[];
}

/** One authenticated stream for the application; hidden tabs retain invalidations without reading lists. */
@Injectable({ providedIn: 'root' })
export class SymlinkLiveService {
  private readonly symlinks = inject(SymlinkService);
  private source: EventSource | null = null;
  private running = false;
  private pageActive = false;
  private dirty = new Set<string>();
  private timer: ReturnType<typeof setTimeout> | null = null;
  private retry: ReturnType<typeof setTimeout> | null = null;
  private delay = 1000;
  private session = 0;
  private reading = false;
  private activeAreas = new Set<string>();
  private readDelay = 150;
  private generation = '';
  private revision = -1;

  start(): void {
    if (this.running) return;
    this.running = true;
    this.session++;
    document.addEventListener('visibilitychange', this.visibility);
    this.connect();
  }

  stop(): void {
    this.running = false;
    this.session++;
    this.source?.close();
    this.source = null;
    if (this.timer !== null) clearTimeout(this.timer);
    if (this.retry !== null) clearTimeout(this.retry);
    this.timer = this.retry = null;
    this.dirty.clear();
    this.symlinks.invalidateReads();
    document.removeEventListener('visibilitychange', this.visibility);
  }

  activatePage(): void {
    this.pageActive = true;
    this.mark(ALL);
  }

  reconcile(): void {
    this.mark(ALL);
  }

  deactivatePage(): void {
    this.pageActive = false;
  }

  private connect(): void {
    if (!this.running) return;
    const source = new EventSource('/api/settings/symlinks/events');
    this.source = source;
    source.onmessage = (message) => {
      if (this.source !== source) return;
      try {
        const change = JSON.parse(message.data) as Change;
        if (!Array.isArray(change.areas) || typeof change.generation !== 'string' || typeof change.revision !== 'number') return;
        if (this.generation !== change.generation) {
          this.generation = change.generation;
          this.revision = -1;
          this.mark(ALL);
        }
        if (change.revision < this.revision) return;
        this.revision = change.revision;
        const areas = change.areas.filter(
          (area) => ALL.includes(area) && !this.symlinks.isAreaCurrent(area, change.generation, change.revision),
        );
        if (areas.length) this.mark(areas);
        this.delay = 1000;
      } catch {
        this.mark(ALL);
      }
    };
    source.onopen = () => {
      if (this.source === source) this.mark(ALL);
    };
    source.onerror = () => {
      if (this.source !== source) return;
      source.close();
      this.source = null;
      console.debug('Symlink event stream disconnected; reconnecting.');
      this.retry = setTimeout(() => {
        this.retry = null;
        this.connect();
      }, this.delay);
      this.delay = Math.min(this.delay * 2, 30000);
    };
  }

  private readonly visibility = (): void => {
    if (!document.hidden) this.mark(ALL);
  };

  private mark(areas: readonly string[]): void {
    for (const area of [...areas, ...this.activeAreas]) this.dirty.add(area);
    this.symlinks.invalidateReads();
    this.schedule();
  }

  private schedule(): void {
    if (!this.running || document.hidden || this.reading || this.timer !== null || !this.dirty.size) return;
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.flush();
    }, this.readDelay);
  }

  private async flush(): Promise<void> {
    if (!this.running || document.hidden) return;
    const session = this.session;
    const areas = this.dirty;
    this.dirty = new Set();
    this.reading = true;
    this.activeAreas = areas;
    try {
      console.debug('Refreshing symlink areas', [...areas]);
      if (this.pageActive) await this.symlinks.refreshAreas(areas);
      if (session !== this.session) return;
      if (areas.has('count') || areas.has('suggestions') || areas.has('settings')) await this.symlinks.refreshCount(true);
      this.readDelay = 150;
    } catch {
      this.readDelay = Math.min(Math.max(1000, this.readDelay * 2), 30000);
      if (session === this.session) for (const area of areas) this.dirty.add(area);
    } finally {
      this.reading = false;
      this.activeAreas = new Set();
      this.schedule();
    }
  }
}
