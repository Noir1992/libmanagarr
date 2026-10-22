export type TableColumnTemplateKind = 'series' | 'season' | 'episode' | 'movie';

export interface TableColumnTemplateDefinition {
  kind: TableColumnTemplateKind;
  key: string;
  header: string;
  showExpandIcon: boolean;
}

export interface TableColumnTemplateContext<T> {
  $implicit: T;
}
