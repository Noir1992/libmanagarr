import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ContentChildren,
  inject,
  input,
  InputSignal,
  QueryList,
  Signal,
  signal,
  WritableSignal,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { MatDialog } from '@angular/material/dialog';
import { isDetailRowActivation, movieDetailData } from '../../utils/media-detail-utils';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';
import { RadarrTableMovieType, RadarrTableType } from '../../interfaces/arr-table/radarr-table-movie-type';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { TableColumnTemplateDirective } from '../table-column-template.directive';

@Component({
  selector: 'app-radarr-table',
  imports: [MatTableModule, AppDatePipe, MatIconButton, NgTemplateOutlet],
  templateUrl: './radarr-table.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './radarr-table.widget.scss',
})
export class RadarrTableWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialog = inject(MatDialog);

  @ContentChildren(TableColumnTemplateDirective<RadarrTableMovieType>)
  private readonly projectedColumns?: QueryList<TableColumnTemplateDirective>;

  protected readonly updatedLabel: Signal<string> = computed((): string => this.i18n.t('table.updated'));
  protected getMovieCountLabel(count: number): string {
    return this.i18n.tCount(count, 'common.movieCountOne', 'common.movieCount');
  }

  readonly table: InputSignal<RadarrTableType> = input.required<RadarrTableType>();

  protected readonly collapsed: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly movieColumns: Signal<readonly TableColumnTemplateDirective<RadarrTableMovieType>[]> = computed(
    (): readonly TableColumnTemplateDirective<RadarrTableMovieType>[] =>
      (this.projectedColumns || []) as readonly TableColumnTemplateDirective<RadarrTableMovieType>[],
  );
  protected readonly movieColumnIds: Signal<readonly string[]> = computed((): readonly string[] =>
    this.movieColumns().map((column) => column.appTableColumnConfig().key),
  );

  protected readonly trackByMovieId = (_index: number, row: RadarrTableMovieType): number => row.id;

  protected toggleCollapsed(): void {
    this.collapsed.update((collapsed) => !collapsed);
  }

  protected openMovieDetails(movie: RadarrTableMovieType, event: Event): void {
    if (!isDetailRowActivation(event)) return;
    this.dialog.open(MediaDetailsDialogWidget, {
      data: movieDetailData(movie, this.table().instanceName),
      width: '700px',
      maxWidth: '95vw',
    });
  }
}
