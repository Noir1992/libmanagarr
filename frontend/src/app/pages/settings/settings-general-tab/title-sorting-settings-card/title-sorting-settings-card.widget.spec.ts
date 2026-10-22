import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TitleSortingSettings } from '../../../../interfaces/settings/title-sorting-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { TitleSortingSettingsCardWidget } from './title-sorting-settings-card.widget';

describe('TitleSortingSettingsCardWidget', () => {
  let fixture: ComponentFixture<TitleSortingSettingsCardWidget>;
  let card: TitleSortingSettingsCardWidget;
  let saved: ReturnType<typeof signal<TitleSortingSettings>>;
  let save: jasmine.Spy;

  beforeEach(async () => {
    saved = signal<TitleSortingSettings>({ enabled: false, ignoredLeadingWords: ['The'] });
    save = jasmine.createSpy('saveTitleSorting').and.callFake(async (value: TitleSortingSettings) => saved.set(value));
    await TestBed.configureTestingModule({
      imports: [TitleSortingSettingsCardWidget],
      providers: [
        { provide: AppI18nService, useValue: { t: (key: string) => key } },
        { provide: AppSettingsService, useValue: { titleSorting: saved, saveTitleSorting: save } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TitleSortingSettingsCardWidget);
    card = fixture.componentInstance;
    await fixture.whenStable();
  });

  async function enterWords(words: string): Promise<HTMLInputElement> {
    const input = fixture.nativeElement.querySelector('#title-sorting-words') as HTMLInputElement;
    input.value = words;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    return input;
  }

  it('shows saved words in one comma-separated input without chips, switches or save buttons', async () => {
    saved.set({ enabled: false, ignoredLeadingWords: ['A', 'The'] });
    await fixture.whenStable();
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('A, The');
    expect(fixture.nativeElement.querySelectorAll('input')).toHaveSize(1);
    expect(fixture.nativeElement.querySelector('mat-chip-set')).toBeNull();
    expect(fixture.nativeElement.querySelector('mat-slide-toggle')).toBeNull();
    const strayButtons = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLElement>).filter(
      (button) => !button.closest('app-info-icon'),
    );
    expect(strayButtons).toHaveSize(0);
  });

  it('keeps edits local and saves a normalized, deduplicated list with Enter', async () => {
    const input = await enterWords(' The , A\u0308, \u00e4, , the, ');
    expect(saved()).toEqual({ enabled: false, ignoredLeadingWords: ['The'] });
    expect(card.hasUnsavedChanges()).toBeTrue();
    expect(save).not.toHaveBeenCalled();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await fixture.whenStable();
    expect(save).toHaveBeenCalledOnceWith({ enabled: true, ignoredLeadingWords: ['The', '\u00c4'] });
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('The, \u00c4');
    expect(card['feedback']()).toBe('settings.titleSorting.saved');
    expect(card.hasUnsavedChanges()).toBeFalse();
  });

  for (const word of ['two words', 'The\u0085Office', 'A\u0000', '\ud800', 'a'.repeat(65)]) {
    it('rejects invalid tokens without saving: ' + JSON.stringify(word), async () => {
      await enterWords('The, ' + word);
      await card['save']();
      expect(card['wordError']()).toBe('settings.titleSorting.invalidWord');
      expect(save).not.toHaveBeenCalled();
      expect(saved().ignoredLeadingWords).toEqual(['The']);
      expect(card['form'].controls.ignoredLeadingWords.value).toBe('The, ' + word);
    });
  }

  it('accepts 64 Unicode codepoints and rejects 101 distinct words', async () => {
    await enterWords('\u{1f600}'.repeat(64));
    await card['save']();
    expect(save).toHaveBeenCalledOnceWith({ enabled: true, ignoredLeadingWords: ['\u{1f600}'.repeat(64)] });
    save.calls.reset();
    await enterWords(Array.from({ length: 101 }, (_, index) => 'word' + index).join(', '));
    await card['save']();
    expect(card['wordError']()).toBe('settings.titleSorting.tooManyWords');
    expect(save).not.toHaveBeenCalled();
  });

  for (const value of ['', '  , , ']) {
    it('saves an empty list when the input contains no words: ' + JSON.stringify(value), async () => {
      await enterWords(value);
      await card['save']();
      expect(save).toHaveBeenCalledOnceWith({ enabled: false, ignoredLeadingWords: [] });
      expect(card['form'].controls.ignoredLeadingWords.value).toBe('');
    });
  }

  it('restores saved text when navigation discards unsaved edits', async () => {
    await enterWords('The, A');
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('The, A');
    card.discardUnsavedChanges();
    expect(card['form'].getRawValue()).toEqual({ ignoredLeadingWords: 'The' });
    expect(card.hasUnsavedChanges()).toBeFalse();
  });

  it('keeps failed edits with field errors and permits retry', async () => {
    await enterWords('The, A');
    save.and.rejectWith(new ValidationProblemError('Invalid word', { 'titleSorting.ignoredLeadingWords[1]': ['Not allowed'] }));
    await card['save']();
    expect(card['wordError']()).toBe('Not allowed');
    expect(card['form'].controls.ignoredLeadingWords.value).toBe('The, A');
    expect(card.hasUnsavedChanges()).toBeTrue();
    expect(card.isBusy()).toBeFalse();
    expect(card['form'].enabled).toBeTrue();
    expect(saved().ignoredLeadingWords).toEqual(['The']);
    save.and.callFake(async (value: TitleSortingSettings) => saved.set(value));
    await card['save']();
    expect(card['feedback']()).toBe('settings.titleSorting.saved');
    expect(card.hasUnsavedChanges()).toBeFalse();
  });

  it('autosaves on blur, enables nonempty words and skips unchanged saves', async () => {
    const input = await enterWords('The, A');
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(save).toHaveBeenCalledOnceWith({ enabled: true, ignoredLeadingWords: ['The', 'A'] });
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(save).toHaveBeenCalledTimes(1);
    await enterWords('');
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(save).toHaveBeenCalledWith({ enabled: false, ignoredLeadingWords: [] });
  });

  it('clears obsolete validation feedback when editing the input', async () => {
    await enterWords('two words');
    await card['save']();
    expect(card['wordError']()).not.toBeNull();
    await enterWords('The, A');
    expect(card['wordError']()).toBeNull();
  });

  it('prevents duplicate saves and disables the input until the request finishes', async () => {
    await enterWords('The, A');
    let resolveSave!: () => void;
    save.and.returnValue(
      new Promise<void>((resolve) => {
        resolveSave = resolve;
      }),
    );
    const pending = card['save']();
    await fixture.whenStable();
    expect(card.isBusy()).toBeTrue();
    expect(card['form'].disabled).toBeTrue();
    expect((fixture.nativeElement.querySelector('#title-sorting-words') as HTMLInputElement).disabled).toBeTrue();
    await card['save']();
    expect(save).toHaveBeenCalledTimes(1);
    resolveSave();
    await pending;
    expect(card.isBusy()).toBeFalse();
  });
});
