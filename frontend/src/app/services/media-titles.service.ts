import { HttpClient } from '@angular/common/http';
import { computed, DestroyRef, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppSettingsService } from './app-settings.service';

/** Initial reads show cached titles immediately; enrichment runs independently. */
@Injectable({ providedIn: 'root' })
export class MediaTitlesService {
  private readonly http = inject(HttpClient);
  private readonly settings = inject(AppSettingsService);
  private readonly version = signal('');
  private sequence = 0;
  readonly revision = computed(() => `${this.settings.language()}:${this.version()}`);

  constructor() {
    const timer = globalThis.setInterval(() => void this.refresh(), 30_000);
    inject(DestroyRef).onDestroy(() => globalThis.clearInterval(timer));
    effect(() => {
      this.settings.language();
      untracked(() => void this.refresh());
    });
  }

  async refresh(): Promise<void> {
    const sequence = ++this.sequence;
    try {
      const result = await firstValueFrom(this.http.post<{ revision: string }>('/api/media-titles/refresh', {}));
      if (sequence === this.sequence) this.version.set(result.revision);
    } catch {
      // Cached titles remain usable; the backend applies bounded retries to unavailable sources.
    }
  }
}
