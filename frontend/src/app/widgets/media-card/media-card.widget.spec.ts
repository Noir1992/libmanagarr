import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MediaGridItem } from '../../interfaces/general/media-grid-item';
import { MediaCardWidget } from './media-card.widget';

@Component({
  imports: [MediaCardWidget],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: ` <app-media-card [item]="item" /> `,
})
class MediaCardHostComponent {
  item: MediaGridItem = {
    kind: 'movie',
    id: 1,
    title: 'The Matrix',
    url: 'http://radarr/movie/the-matrix',
    posterUrl: 'http://localhost/api/poster/1.jpg',
    detailRows: [
      { label: 'Instanz', value: 'Radarr' },
      { label: 'Profil', value: '1080p' },
    ],
  };
}

describe('MediaCardWidget', () => {
  let host: MediaCardHostComponent;
  let fixture: ComponentFixture<MediaCardHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MediaCardHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(MediaCardHostComponent);
    host = fixture.componentInstance;
  });

  describe('hasPoster', () => {
    it('is true when posterUrl is set', () => {
      host.item.posterUrl = 'http://test.jpg';
      fixture.detectChanges();
      const img = fixture.nativeElement.querySelector('img.media-card-poster');
      expect(img).toBeTruthy();
    });

    it('is false when posterUrl is null', () => {
      host.item.posterUrl = null;
      fixture.detectChanges();
      const img = fixture.nativeElement.querySelector('img.media-card-poster');
      expect(img).toBeFalsy();
    });
  });

  describe('posterUrl', () => {
    it('renders the img src from posterUrl', () => {
      host.item.posterUrl = 'http://example.com/poster.jpg';
      fixture.detectChanges();
      const img = fixture.nativeElement.querySelector('img.media-card-poster');
      expect(img.src).toContain('http://example.com/poster.jpg');
    });
  });

  describe('headerBg (hashColor fallback)', () => {
    it('renders a fallback with hash-based background color when no poster', () => {
      host.item.posterUrl = null;
      fixture.detectChanges();
      const fallback = fixture.nativeElement.querySelector('.media-card-fallback');
      expect(fallback).toBeTruthy();
      expect(fallback.style.getPropertyValue('--card-header-bg')).toMatch(/^hsl\(/);
    });
  });

  describe('initials', () => {
    it('shows first two words initials for short titles', () => {
      host.item.title = 'The Matrix';
      host.item.posterUrl = null;
      fixture.detectChanges();
      const initials = fixture.nativeElement.querySelector('.media-card-initials');
      expect(initials.textContent).toBe('TM');
    });

    it('shows first and last word initials for long titles', () => {
      host.item.title = 'The Lord of the Rings';
      host.item.posterUrl = null;
      fixture.detectChanges();
      const initials = fixture.nativeElement.querySelector('.media-card-initials');
      expect(initials.textContent).toBe('TR');
    });

    it('shows single letter for one-word titles', () => {
      host.item.title = 'Avatar';
      host.item.posterUrl = null;
      fixture.detectChanges();
      const initials = fixture.nativeElement.querySelector('.media-card-initials');
      expect(initials.textContent).toBe('A');
    });
  });

  describe('title', () => {
    it('displays the item title as a link', () => {
      host.item.title = 'Inception';
      host.item.url = 'http://test/inception';
      fixture.detectChanges();
      const titleLink = fixture.nativeElement.querySelector('.media-card-title a');
      expect(titleLink.textContent).toBe('Inception');
      expect(titleLink.href).toContain('http://test/inception');
    });
  });

  describe('detailRows', () => {
    it('renders all detail rows', () => {
      host.item.detailRows = [
        { label: 'Instanz', value: 'Radarr' },
        { label: 'Profil', value: '1080p' },
        { label: 'Score', value: '85' },
      ];
      fixture.detectChanges();
      const details = fixture.nativeElement.querySelectorAll('.media-card-detail-row');
      expect(details).toHaveSize(3);
    });

    it('renders label and value for each row', () => {
      host.item.detailRows = [{ label: 'Label', value: 'Value' }];
      fixture.detectChanges();
      const label = fixture.nativeElement.querySelector('.media-card-detail-row-label');
      const value = fixture.nativeElement.querySelector('.media-card-detail-row-value');
      expect(label.textContent).toBe('Label');
      expect(value.textContent).toBe('Value');
    });

    it('applies the matching tone class to the value of each row', () => {
      host.item.detailRows = [
        { label: 'Aired status', value: 'released', tone: 'success' },
        { label: 'Availability', value: 'Missing', tone: 'danger' },
        { label: 'Status', value: 'announced', tone: 'warning' },
        { label: 'Aired status', value: 'continuing', tone: 'info' },
      ];
      fixture.detectChanges();
      const values = fixture.nativeElement.querySelectorAll('.media-card-detail-row-value');
      expect(values).toHaveSize(4);
      expect(values[0].classList).toContain('tone-success');
      expect(values[1].classList).toContain('tone-danger');
      expect(values[2].classList).toContain('tone-warning');
      expect(values[3].classList).toContain('tone-info');
    });

    it('does not apply a tone class when the row has no tone', () => {
      host.item.detailRows = [{ label: 'Instance', value: 'Radarr' }];
      fixture.detectChanges();
      const value = fixture.nativeElement.querySelector('.media-card-detail-row-value');
      expect(value.classList).not.toContain('tone-success');
      expect(value.classList).not.toContain('tone-warning');
      expect(value.classList).not.toContain('tone-danger');
      expect(value.classList).not.toContain('tone-info');
    });
  });
});
