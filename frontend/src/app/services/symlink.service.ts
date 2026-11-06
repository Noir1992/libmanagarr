import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable, signal, WritableSignal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SymlinkCapabilityCheck, SymlinkSettings } from '../interfaces/symlinks/symlink-settings';
import { SymlinkMatchingRun, SymlinkSuggestion } from '../interfaces/symlinks/symlink-suggestion';
import { SymlinkLink, SymlinkLinkPlan } from '../interfaces/symlinks/symlink-link';
import { PreviewSymlinkLinkRequest, SpecialMoviePairRequest, SymlinkRejection } from '../interfaces/symlinks/symlink-decisions';
import { TranslationKey } from './i18n/_i18n-key';
import { AppI18nService } from './app-i18n.service';

/** One action on one row: busy while it runs, the answer afterwards. */
export interface SymlinkActionState {
  busy?: boolean;
  error?: string | null;
  success?: boolean;
}

/**
 * The whole symlink area behind one set of signals: the path configuration with its capability answers,
 * the suggestions, the register of managed links and the archive of declines. Reads are cheap — the
 * backend serves them from the settings snapshot and the stored rows — so a reload after a write is the
 * normal way to learn what the row looks like now instead of guessing it here.
 */
@Injectable({ providedIn: 'root' })
export class SymlinkService {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(AppI18nService);
  private readonly url = '/api/settings/symlinks';

  private readonly settingsState = signal<SymlinkSettings | null>(null);
  private readonly suggestionsState = signal<SymlinkSuggestion[]>([]);
  private readonly linksState = signal<SymlinkLink[]>([]);
  private readonly rejectionsState = signal<SymlinkRejection[]>([]);
  private readonly suggestionBusy = signal<Record<string, SymlinkActionState>>({});
  private readonly linkBusy = signal<Record<string, SymlinkActionState>>({});
  private readonly capabilityBusy = signal<Record<string, SymlinkActionState>>({});

  readonly settings = this.settingsState.asReadonly();
  readonly suggestions = this.suggestionsState.asReadonly();
  readonly links = this.linksState.asReadonly();
  readonly rejections = this.rejectionsState.asReadonly();
  readonly suggestionStates = this.suggestionBusy.asReadonly();
  readonly linkStates = this.linkBusy.asReadonly();
  readonly capabilityStates = this.capabilityBusy.asReadonly();

  readonly settingsLoading = signal(false);
  readonly settingsError = signal<string | null>(null);
  readonly saving = signal(false);
  /** The refused field of a validation answer, so the form can put the sentence where it belongs. */
  readonly fieldErrors = signal<Record<string, string>>({});

  readonly overviewLoading = signal(false);
  readonly overviewError = signal<string | null>(null);
  readonly refreshing = signal(false);
  /** The summary of the last manual matching pass; a pass that changed nothing is a result too. */
  readonly lastRun = signal<SymlinkMatchingRun | null>(null);

  async loadSettings(): Promise<void> {
    this.settingsLoading.set(true);
    this.settingsError.set(null);
    try {
      this.settingsState.set(await firstValueFrom(this.http.get<SymlinkSettings>(this.url)));
    } catch (error) {
      this.settingsError.set(this.message(error, 'settings.symlinks.loadError'));
      throw error;
    } finally {
      this.settingsLoading.set(false);
    }
  }

  /**
   * Saves the path configuration. The revision stays with the server, because it is what invalidates the
   * stored probe answers, and a refused save keeps the draft as typed: rebuilding the form from the
   * server here would throw away the edit that was meant to fix it.
   */
  async saveSettings(settings: SymlinkSettings): Promise<SymlinkSettings> {
    const { storageAreas, pathMappings, readerViews } = settings;
    this.saving.set(true);
    this.settingsError.set(null);
    this.fieldErrors.set({});
    try {
      const saved = await firstValueFrom(this.http.put<SymlinkSettings>(this.url, { storageAreas, pathMappings, readerViews }));
      this.settingsState.set(saved);
      return saved;
    } catch (error) {
      this.fieldErrors.set(this.fields(error));
      const message = this.message(error, 'settings.symlinks.saveError');
      this.settingsError.set(message);
      throw new Error(message, { cause: error });
    } finally {
      this.saving.set(false);
    }
  }

  /** Probes one storage area; the answer replaces the stored one for that area, stale or not. */
  async testCapability(storageAreaId: string): Promise<SymlinkCapabilityCheck> {
    return this.perform(this.capabilityBusy, storageAreaId, () =>
      firstValueFrom(
        this.http.post<SymlinkCapabilityCheck>(
          `${this.url}/storage-areas/${encodeURIComponent(storageAreaId)}/capability-test`,
          null,
        ),
      ).then((check) => {
        this.settingsState.update((settings) =>
          settings === null ? settings : { ...settings, capabilityChecks: this.withCheck(settings.capabilityChecks, check) },
        );
        return check;
      }),
    );
  }

  async loadOverview(): Promise<void> {
    this.overviewLoading.set(true);
    this.overviewError.set(null);
    try {
      await Promise.all([this.readSuggestions(), this.readLinks(), this.readRejections()]);
    } catch (error) {
      this.overviewError.set(this.message(error, 'symlinks.loadError'));
      throw error;
    } finally {
      this.overviewLoading.set(false);
    }
  }

  /** The manual pass over the snapshots. It re-reads even when nothing looks changed. */
  async refreshSuggestions(): Promise<SymlinkMatchingRun> {
    this.refreshing.set(true);
    this.overviewError.set(null);
    try {
      const run = await firstValueFrom(this.http.post<SymlinkMatchingRun>(`${this.url}/suggestions/refresh`, null));
      this.lastRun.set(run);
      if (run.didWork) await this.reload(() => this.readSuggestions());
      return run;
    } catch (error) {
      const message = this.message(error, 'symlinks.refreshError');
      this.overviewError.set(message);
      throw new Error(message, { cause: error });
    } finally {
      this.refreshing.set(false);
    }
  }

  /** What confirming a pair would do; it writes neither a row nor a file, so looking is free. */
  previewLink(request: PreviewSymlinkLinkRequest): Promise<SymlinkLinkPlan> {
    return firstValueFrom(this.http.post<SymlinkLinkPlan>(`${this.url}/preview`, request));
  }

  async confirmSuggestion(id: string, configRevision: number): Promise<SymlinkLink> {
    const link = await this.perform(
      this.suggestionBusy,
      id,
      () => firstValueFrom(this.http.post<SymlinkLink>(`${this.url}/suggestions/${encodeURIComponent(id)}/confirm`, { configRevision })),
      // A refused revision is the path configuration having moved, so the form and the answers age out too,
      // and the offer list is read again rather than left there to earn the same refusal a second time.
      () => Promise.all([this.loadSettings(), this.readSuggestions()]),
    );
    this.linksState.update((links) => [link, ...links.filter((existing) => existing.id !== link.id)]);
    await this.reload(() => this.readSuggestions());
    return link;
  }

  async rejectSuggestion(id: string, detail: string | null): Promise<SymlinkSuggestion> {
    const rejected = await this.perform(this.suggestionBusy, id, () =>
      firstValueFrom(this.http.post<SymlinkSuggestion>(`${this.url}/suggestions/${encodeURIComponent(id)}/reject`, { detail })),
    );
    this.suggestionsState.update((suggestions) => suggestions.map((suggestion) => (suggestion.id === id ? rejected : suggestion)));
    await this.reload(() => this.readRejections());
    return rejected;
  }

  /** Forgets one decline, which lets the next pass offer the pair again. */
  async reactivateRejection(pair: SpecialMoviePairRequest): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${this.url}/rejections/reactivate`, pair));
    this.rejectionsState.update((rejections) =>
      rejections.filter(
        (rejection) =>
          rejection.radarrInstanceId !== pair.radarrInstanceId ||
          rejection.radarrMovieId !== pair.radarrMovieId ||
          rejection.sonarrInstanceId !== pair.sonarrInstanceId ||
          rejection.sonarrSeriesId !== pair.sonarrSeriesId ||
          rejection.sonarrEpisodeId !== pair.sonarrEpisodeId,
      ),
    );
    await this.reload(() => this.readSuggestions());
  }

  /**
   * Looks at one row now. It is the same comparison the scheduled pass runs over the whole register, so the
   * answer is the row as the register holds it afterwards — with what the look found, not with what was hoped.
   */
  async checkLink(id: string): Promise<SymlinkLink> {
    const checked = await this.perform(
      this.linkBusy,
      id,
      () => firstValueFrom(this.http.post<SymlinkLink>(`${this.url}/links/${encodeURIComponent(id)}/check`, null)),
      () => this.readLinks(),
    );
    this.linksState.update((links) => links.map((existing) => (existing.id === checked.id ? checked : existing)));
    return checked;
  }

  async setFollowUp(id: string, autoFollowUp: boolean, revision: number): Promise<SymlinkLink> {
    const updated = await this.perform(
      this.linkBusy,
      id,
      () => firstValueFrom(this.http.put<SymlinkLink>(`${this.url}/links/${encodeURIComponent(id)}/follow-up`, { autoFollowUp, revision })),
      () => this.readLinks(),
    );
    this.linksState.update((links) => links.map((existing) => (existing.id === updated.id ? updated : existing)));
    return updated;
  }

  async deleteLink(id: string, revision: number): Promise<void> {
    await this.perform(
      this.linkBusy,
      id,
      () =>
        firstValueFrom(
          this.http.delete<void>(`${this.url}/links/${encodeURIComponent(id)}`, {
            params: new HttpParams().set('revision', String(revision)),
          }),
        ),
      () => this.readLinks(),
    );
    this.linksState.update((links) => links.filter((link) => link.id !== id));
  }

  private readSuggestions(): Promise<void> {
    return firstValueFrom(this.http.get<SymlinkSuggestion[]>(`${this.url}/suggestions`)).then((suggestions) =>
      this.suggestionsState.set(suggestions),
    );
  }

  private readRejections(): Promise<void> {
    return firstValueFrom(this.http.get<SymlinkRejection[]>(`${this.url}/rejections`)).then((rejections) =>
      this.rejectionsState.set(rejections),
    );
  }

  private readLinks(): Promise<void> {
    return firstValueFrom(this.http.get<SymlinkLink[]>(`${this.url}/links`)).then((links) => this.linksState.set(links));
  }

  /**
   * Runs a reload whose failure may not undo the action that earned it: the row changed either way, so
   * the read problem is reported on its own instead of as a failed action.
   */
  private async reload(reload: () => Promise<unknown>): Promise<void> {
    try {
      await reload();
    } catch (error) {
      this.overviewError.set(this.message(error, 'symlinks.loadError'));
    }
  }

  private withCheck(checks: SymlinkCapabilityCheck[], check: SymlinkCapabilityCheck): SymlinkCapabilityCheck[] {
    return [...checks.filter((existing) => existing.storageAreaId !== check.storageAreaId), check];
  }

  /**
   * Runs one action on one row. A row that moved underneath the view answers 409, and the answer to that
   * is fresh rows while the conflict stays visible — the next attempt then presents the revision the
   * server is holding instead of the one this view remembers.
   */
  private async perform<T>(
    states: WritableSignal<Record<string, SymlinkActionState>>,
    key: string,
    request: () => Promise<T>,
    conflict?: () => Promise<unknown>,
  ): Promise<T> {
    this.updateState(states, key, { busy: true, error: null, success: false });
    try {
      const result = await request();
      this.updateState(states, key, { success: true });
      return result;
    } catch (error) {
      const message = this.message(error, 'symlinks.actionError');
      if (error instanceof HttpErrorResponse && error.status === 409 && conflict) {
        await this.reload(conflict);
      }
      this.updateState(states, key, { error: message });
      throw new Error(message, { cause: error });
    } finally {
      this.updateState(states, key, { busy: false });
    }
  }

  private updateState(states: WritableSignal<Record<string, SymlinkActionState>>, key: string, value: SymlinkActionState): void {
    states.update((current) => ({ ...current, [key]: { ...current[key], ...value } }));
  }

  private fields(error: unknown): Record<string, string> {
    if (!(error instanceof HttpErrorResponse)) return {};
    const flattened: Record<string, string> = {};
    for (const [field, messages] of Object.entries((error.error as { errors?: Record<string, string[]> } | null)?.errors ?? {})) {
      const first = messages[0];
      if (first) flattened[field] = first;
    }
    return flattened;
  }

  private message(error: unknown, fallbackKey: TranslationKey): string {
    if (error instanceof HttpErrorResponse) {
      const problem = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
      return (
        Object.values(problem?.errors ?? {})
          .flat()
          .join(' ') ||
        problem?.detail ||
        problem?.title ||
        this.i18n.t(fallbackKey)
      );
    }
    return error instanceof Error ? error.message : this.i18n.t(fallbackKey);
  }
}
