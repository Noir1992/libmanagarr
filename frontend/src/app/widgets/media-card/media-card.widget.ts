import { ChangeDetectionStrategy, Component, computed, inject, input, InputSignal, Signal, signal, WritableSignal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MediaGridItem } from '../../interfaces/general/media-grid-item';
import { hashColor } from '../../utils/base-utils';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';
import { SonarrSeriesDialogWidget } from '../sonarr-series-dialog/sonarr-series-dialog.widget';

@Component({
  selector: 'app-media-card',
  imports: [],
  templateUrl: './media-card.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './media-card.widget.scss',
})
export class MediaCardWidget {
  private readonly dialog: MatDialog = inject(MatDialog);

  readonly item: InputSignal<MediaGridItem> = input.required<MediaGridItem>();
  readonly clickable: InputSignal<boolean> = input(false);

  protected readonly isPressed: WritableSignal<boolean> = signal(false);

  protected onMousedown(): void {
    if (this.clickable()) {
      this.isPressed.set(true);
    }
  }

  protected onMouseup(): void {
    this.isPressed.set(false);
  }

  protected onCardKeydown(event: Event): void {
    if (event.target !== event.currentTarget || !this.clickable()) return;
    event.preventDefault();
    this.onCardClick();
  }

  protected onCardClick(): void {
    if (this.clickable() && this.item().seriesData) {
      this.dialog.open(SonarrSeriesDialogWidget, { data: this.item(), width: '700px', maxWidth: '95vw' });
    } else if (this.clickable() && this.item().movieData) {
      this.dialog.open(MediaDetailsDialogWidget, { data: this.item().movieData, width: '700px', maxWidth: '95vw' });
    }
  }

  protected readonly hasPoster: Signal<boolean> = computed(() => !!this.item().posterUrl);
  protected readonly posterUrl: Signal<string | null> = computed(() => this.item().posterUrl);
  protected readonly headerBg: Signal<string> = computed(() => {
    return hashColor(this.item().title);
  });

  protected readonly initials: Signal<string> = computed(() => {
    const words: string[] = this.item().title.split(/\s+/);
    if (words.length <= 2) {
      return words
        .map((w) => w[0])
        .join('')
        .toUpperCase();
    }
    return words[0][0] + words.at(-1)![0];
  });
}
