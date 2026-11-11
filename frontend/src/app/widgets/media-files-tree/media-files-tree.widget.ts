import { Component, computed, EventEmitter, HostListener, inject, Input, Output, Signal, ChangeDetectionStrategy } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MediaTreeNode } from '../../interfaces/media-tree/media-tree-node';
import { FormatBytesPipe } from '../../pipes/format-bytes.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { codecFileDetailData } from '../../utils/media-detail-utils';
import { openErrorToast, openSuccessToast } from '../../utils/feedback-toast-utils';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';

type DisplayTreeNode = {
  node: MediaTreeNode;
  depth: number;
  isRoot: boolean;
};

@Component({
  selector: 'app-media-files-tree',
  imports: [FormatBytesPipe, MatIconButton],
  templateUrl: './media-files-tree.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './media-files-tree.widget.scss',
})
export class MediaFilesTreeWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly snackBar: MatSnackBar = inject(MatSnackBar);
  private readonly dialog: MatDialog = inject(MatDialog);

  protected readonly optionsLabel: Signal<string> = computed((): string => this.i18n.t('common.options'));
  protected readonly copyNameLabel: Signal<string> = computed((): string => this.i18n.t('codecStats.copyName'));
  protected readonly openInArrLabel = (node: MediaTreeNode): string => {
    return this.i18n.t('codecStats.openIn', { name: node.source });
  };

  @Input({ required: true }) scopeKey!: string;
  @Input({ required: true }) roots!: readonly MediaTreeNode[];
  @Output() readonly interaction: EventEmitter<void> = new EventEmitter<void>();

  protected readonly expandedTreeNodes: Set<string> = new Set<string>();
  protected openContextMenuKey: string | null = null;

  protected toggleTreeNode(nodeKey: string): void {
    if (this.expandedTreeNodes.has(nodeKey)) {
      this.expandedTreeNodes.delete(nodeKey);
    } else {
      this.expandedTreeNodes.add(nodeKey);
    }
  }

  protected isTreeNodeExpanded(nodeKey: string): boolean {
    return this.expandedTreeNodes.has(nodeKey);
  }

  protected toggleNodeContextMenu(node: MediaTreeNode, event: Event): void {
    event.stopPropagation();
    this.openContextMenuKey = this.openContextMenuKey === node.key ? null : node.key;
  }

  protected isNodeContextMenuOpen(node: MediaTreeNode): boolean {
    return this.openContextMenuKey === node.key;
  }

  protected async copyNodeName(node: MediaTreeNode): Promise<void> {
    const copied: boolean = await this.copyTextToClipboard(node.label);
    this.closeContextMenuAndNotify();
    if (copied) {
      openSuccessToast(this.snackBar, this.i18n.t('codecStats.copySuccess'));
    } else {
      openErrorToast(this.snackBar, this.i18n.t('codecStats.copySuccess'));
    }
  }

  protected openNodeInArr(node: MediaTreeNode): void {
    if (!node.url) {
      return;
    }

    this.closeContextMenuAndNotify();
    globalThis.open(node.url, '_blank', 'noopener,noreferrer');
  }

  protected openFileDetails(node: MediaTreeNode): void {
    if (!node.file) {
      return;
    }

    this.interaction.emit();
    this.dialog.open(MediaDetailsDialogWidget, {
      data: codecFileDetailData(node.file, node.url ?? null),
      width: '700px',
      maxWidth: '95vw',
    });
  }

  protected visibleTreeNodes(roots: readonly MediaTreeNode[]): DisplayTreeNode[] {
    const result: DisplayTreeNode[] = [];
    const sortedRoots: MediaTreeNode[] = [...roots].sort((left, right) => this.compareRootNodes(left, right));

    for (const root of sortedRoots) {
      this.collectVisibleNodes(root, 0, true, result);
    }

    return result;
  }

  @HostListener('document:click', ['$event'])
  protected closeContextMenu(event: MouseEvent): void {
    const target: EventTarget | null = event.target;
    let originElement: Element | null = null;
    if (target instanceof Element) {
      originElement = target;
    } else if (target instanceof Node) {
      originElement = target.parentElement;
    }

    if (originElement?.closest('.tree-row-actions')) {
      return;
    }

    this.openContextMenuKey = null;
  }

  private collectVisibleNodes(node: MediaTreeNode, depth: number, isRoot: boolean, result: DisplayTreeNode[]): void {
    result.push({ node, depth, isRoot });

    if (!node.isDirectory || !this.isTreeNodeExpanded(node.key)) {
      return;
    }

    const sortedChildren: readonly MediaTreeNode[] = [...node.children].sort((a, b) => {
      return this.compareNodes(a, b);
    });

    for (const child of sortedChildren) {
      this.collectVisibleNodes(child, depth + 1, false, result);
    }
  }

  private compareRootNodes(left: MediaTreeNode, right: MediaTreeNode): number {
    const sourceOrder: number = this.sourceOrder(left.source) - this.sourceOrder(right.source);
    if (sourceOrder !== 0) {
      return sourceOrder;
    }

    const instanceOrder: number = (left.instanceName ?? '').localeCompare(right.instanceName ?? '');
    if (instanceOrder !== 0) {
      return instanceOrder;
    }

    return this.compareNodes(left, right);
  }

  private compareNodes(left: MediaTreeNode, right: MediaTreeNode): number {
    if (left.isDirectory !== right.isDirectory) {
      return left.isDirectory ? -1 : 1;
    }

    return left.label.localeCompare(right.label);
  }

  private sourceOrder(source?: string): number {
    const normalized: string = (source ?? '').trim().toLowerCase();
    if (normalized.startsWith('radarr')) {
      return 0;
    }

    if (normalized.startsWith('sonarr')) {
      return 1;
    }

    return 2;
  }

  private async copyTextToClipboard(text: string): Promise<boolean> {
    if (!navigator.clipboard?.writeText) {
      return false;
    }

    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      return false;
    }
  }

  private closeContextMenuAndNotify(): void {
    this.openContextMenuKey = null;
    this.interaction.emit();
  }
}
