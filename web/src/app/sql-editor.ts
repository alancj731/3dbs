import { Component, ElementRef, OnDestroy, afterNextRender, effect, input, model, output, viewChild } from '@angular/core';
import { basicSetup } from 'codemirror';
import { Compartment, EditorState, Prec } from '@codemirror/state';
import { EditorView, keymap, placeholder } from '@codemirror/view';
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { MSSQL, MySQL, PostgreSQL, SQLNamespace, sql } from '@codemirror/lang-sql';
import { tags as t } from '@lezer/highlight';
import { DbKey, TableInfo } from './api.service';

const DIALECTS = { sqlserver: MSSQL, mysql: MySQL, postgres: PostgreSQL };

// Colors come from CSS variables so the editor follows the page's light/dark theme.
const highlight = HighlightStyle.define([
  { tag: t.keyword, color: 'var(--syn-keyword)', fontWeight: '600' },
  { tag: [t.string, t.special(t.string)], color: 'var(--syn-string)' },
  { tag: [t.number, t.bool, t.null], color: 'var(--syn-number)' },
  { tag: [t.lineComment, t.blockComment], color: 'var(--syn-comment)', fontStyle: 'italic' },
  { tag: [t.typeName, t.standard(t.name)], color: 'var(--syn-type)' },
  { tag: [t.operator, t.punctuation], color: 'var(--text-muted)' },
]);

const theme = EditorView.theme({
  '&': { backgroundColor: 'var(--editor-bg)', color: 'var(--text)', fontSize: '14px', height: '100%' },
  '.cm-scroller': { fontFamily: 'var(--mono)', lineHeight: '1.55' },
  '.cm-content': { caretColor: 'var(--accent)', padding: '10px 0' },
  '.cm-gutters': { backgroundColor: 'var(--editor-bg)', color: 'var(--text-faint)', border: 'none' },
  '.cm-activeLine, .cm-activeLineGutter': { backgroundColor: 'var(--editor-active)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': { backgroundColor: 'var(--editor-selection) !important' },
  '.cm-cursor': { borderLeftColor: 'var(--accent)' },
  '.cm-placeholder': { color: 'var(--text-faint)' },
  '.cm-tooltip': { backgroundColor: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)' },
  '.cm-tooltip-autocomplete ul li[aria-selected]': { backgroundColor: 'var(--accent)', color: 'var(--on-accent)' },
});

@Component({
  selector: 'app-sql-editor',
  template: `<div class="editor-host" #host></div>`,
})
export class SqlEditor implements OnDestroy {
  readonly value = model('');
  readonly dialect = input<DbKey>('sqlserver');
  readonly tables = input<TableInfo[]>([]);
  readonly run = output<void>();
  readonly check = output<void>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly language = new Compartment();
  private view?: EditorView;

  constructor() {
    afterNextRender(() => {
      this.view = new EditorView({
        parent: this.host().nativeElement,
        state: EditorState.create({
          doc: this.value(),
          extensions: [
            Prec.highest(keymap.of([
              { key: 'Mod-Enter', run: () => (this.run.emit(), true) },
              { key: 'Shift-Mod-Enter', run: () => (this.check.emit(), true) },
            ])),
            basicSetup,
            EditorView.lineWrapping,
            theme,
            syntaxHighlighting(highlight),
            placeholder('Write your SQL here…  (Ctrl+Enter runs it, Ctrl+Shift+Enter checks the answer)'),
            this.language.of(this.languageFor()),
            EditorView.updateListener.of(u => {
              if (u.docChanged) this.value.set(u.state.doc.toString());
            }),
          ],
        }),
      });
    });

    // Switch highlighting/completion when the database or its schema changes.
    effect(() => {
      const ext = this.languageFor();
      this.view?.dispatch({ effects: this.language.reconfigure(ext) });
    });

    // Push external changes (switching exercise, "copy answer to editor") into the editor.
    effect(() => {
      const v = this.value();
      if (this.view && v !== this.view.state.doc.toString()) {
        this.view.dispatch({ changes: { from: 0, to: this.view.state.doc.length, insert: v } });
      }
    });
  }

  /** Insert text at the cursor, e.g. a table name clicked in the schema panel. */
  insert(text: string) {
    if (!this.view) return;
    const { from, to } = this.view.state.selection.main;
    this.view.dispatch({ changes: { from, to, insert: text }, selection: { anchor: from + text.length } });
    this.view.focus();
  }

  /** The selected text, or the whole query when nothing is selected. */
  selectionOrAll(): string {
    if (!this.view) return this.value();
    const { from, to } = this.view.state.selection.main;
    return from === to ? this.view.state.doc.toString() : this.view.state.sliceDoc(from, to);
  }

  focus() {
    this.view?.focus();
  }

  ngOnDestroy() {
    this.view?.destroy();
  }

  private languageFor() {
    const columns = Object.fromEntries(this.tables().map(tb => [tb.name, tb.columns.map(c => c.name)]));
    const schema: SQLNamespace = { SalesLT: columns, ...columns };
    return sql({ dialect: DIALECTS[this.dialect()], schema, upperCaseKeywords: true });
  }
}
