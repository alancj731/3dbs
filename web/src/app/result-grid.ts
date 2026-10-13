import { Component, input } from '@angular/core';

@Component({
  selector: 'app-result-grid',
  template: `
    @if (columns().length) {
      <div class="grid-wrap">
        <table class="grid">
          <thead>
            <tr>
              <th class="rownum">#</th>
              @for (c of columns(); track $index) { <th>{{ c }}</th> }
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track $index; let i = $index) {
              <tr>
                <td class="rownum">{{ i + 1 }}</td>
                @for (v of row; track $index) {
                  @if (v === null) { <td class="null">NULL</td> }
                  @else { <td [class.num]="isNumber(v)">{{ v }}</td> }
                }
              </tr>
            } @empty {
              <tr><td class="empty" [attr.colspan]="columns().length + 1">No rows</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class ResultGrid {
  readonly columns = input<string[]>([]);
  readonly rows = input<unknown[][]>([]);

  isNumber(v: unknown) {
    return typeof v === 'number';
  }
}
