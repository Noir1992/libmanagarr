import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppLanguage } from '../interfaces/general/app-language';
import { AppSettingsService } from './app-settings.service';
import { MediaTitlesService } from './media-titles.service';

describe('MediaTitlesService', () => {
  const url = '/api/media-titles/refresh';
  let http: HttpTestingController;
  let service: MediaTitlesService;
  let language: ReturnType<typeof signal<AppLanguage>>;

  beforeEach(() => {
    language = signal<AppLanguage>('en');
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AppSettingsService, useValue: { language } }],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(MediaTitlesService);
    TestBed.tick();
  });

  afterEach(() => http.verify());

  it('publishes completed enrichment while keeping the language reactive', () => {
    expect(service.revision()).toBe('en:');
    http.expectOne(url).flush({ revision: 'saved' });
    return Promise.resolve().then(() => {
      expect(service.revision()).toBe('en:saved');
      language.set('de');
      TestBed.tick();
      expect(service.revision()).toBe('de:saved');
      http.expectOne(url).flush({ revision: 'updated' });
    });
  });

  it('ignores late responses after rapid language changes', async () => {
    const english = http.expectOne(url);
    language.set('de');
    TestBed.tick();
    const german = http.expectOne(url);
    language.set('fr');
    TestBed.tick();
    http.expectOne(url).flush({ revision: 'newest' });
    await Promise.resolve();
    german.flush({ revision: 'old' });
    english.flush({ revision: 'older' });
    await Promise.resolve();
    expect(service.revision()).toBe('fr:newest');
  });

  it('leaves cached titles available when enrichment fails', async () => {
    http.expectOne(url).flush({}, { status: 503, statusText: 'Unavailable' });
    await Promise.resolve();
    expect(service.revision()).toBe('en:');
  });
});
