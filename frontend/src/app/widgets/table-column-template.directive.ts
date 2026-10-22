import { Directive, inject, input, InputSignal, TemplateRef } from '@angular/core';
import { TableColumnTemplateContext, TableColumnTemplateDefinition } from '../interfaces/general/table-column-template';

@Directive({
  selector: 'ng-template[appTableColumn]',
})
export class TableColumnTemplateDirective<T = unknown> {
  readonly appTableColumnConfig: InputSignal<TableColumnTemplateDefinition> = input.required<TableColumnTemplateDefinition>();
  readonly templateRef: TemplateRef<TableColumnTemplateContext<T>> = inject(TemplateRef<TableColumnTemplateContext<T>>);
}
