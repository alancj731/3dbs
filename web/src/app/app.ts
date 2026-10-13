import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Answer, ApiService, CheckResult, DATABASES, DbKey, Exercise, RunOutcome, TableInfo } from './api.service';
import { Progress } from './progress';
import { ResultGrid } from './result-grid';
import { SqlEditor } from './sql-editor';

@Component({
  selector: 'app-root',
  imports: [SqlEditor, ResultGrid],
  templateUrl: './app.html',
})
export class App {
  private readonly api = inject(ApiService);
  protected readonly progress = inject(Progress);
  protected readonly editor = viewChild.required(SqlEditor);
  protected readonly aboutDialog = viewChild.required<ElementRef<HTMLDialogElement>>('about');
  protected readonly repoUrl = 'https://github.com/alancj731/3dbs';

  protected readonly databases = DATABASES;
  protected readonly exercises = signal<Exercise[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly db = signal<DbKey>(this.progress.lastDb());
  /** null = free SQL mode */
  protected readonly currentId = signal<number | null>(initialId());
  protected readonly sql = signal('');
  protected readonly filter = signal('');

  protected readonly busy = signal<'run' | 'check' | null>(null);
  /** The request has been running for a while: say so instead of looking stuck. */
  protected readonly slow = signal(false);
  private slowTimer?: ReturnType<typeof setTimeout>;
  protected readonly runOutcome = signal<RunOutcome | null>(null);
  protected readonly checkResult = signal<CheckResult | null>(null);
  protected readonly requestError = signal<string | null>(null);

  protected readonly showHint = signal(false);
  protected readonly confirmReveal = signal(false);
  protected readonly answer = signal<Answer | null>(null);

  protected readonly schema = signal<TableInfo[]>([]);
  protected readonly showSchema = signal(false);
  protected readonly clipboardMessage = signal<string | null>(null);
  private flashTimer?: ReturnType<typeof setTimeout>;
  protected readonly openTables = signal<Set<string>>(new Set());

  protected readonly current = computed(() => this.exercises().find(e => e.id === this.currentId()) ?? null);
  protected readonly index = computed(() => this.exercises().findIndex(e => e.id === this.currentId()));
  protected readonly dbLabel = computed(() => DATABASES.find(d => d.key === this.db())!.label);

  protected readonly groups = computed(() => {
    const q = this.filter().trim().toLowerCase();
    const groups: { category: string; items: Exercise[] }[] = [];
    for (const e of this.exercises()) {
      if (q && !`${e.id} ${e.title} ${e.category} ${e.prompt}`.toLowerCase().includes(q)) continue;
      const g = groups.at(-1)?.category === e.category ? groups.at(-1)! : (groups.push({ category: e.category, items: [] }), groups.at(-1)!);
      g.items.push(e);
    }
    return groups;
  });

  protected readonly solvedCount = computed(() => this.progress.solved()[this.db()].length);

  constructor() {
    this.api.exercises().subscribe({
      next: list => {
        this.exercises.set(list);
        if (this.currentId() !== null && !list.some(e => e.id === this.currentId())) this.currentId.set(list[0]?.id ?? null);
      },
      error: () => this.loadError.set('Could not reach the API. Is it running?  (dotnet run --project api)'),
    });

    // Exercise or database changed: load the saved draft and clear the old results.
    effect(() => {
      const db = this.db();
      const id = this.currentId();
      untracked(() => {
        this.sql.set(this.progress.draft(db, id));
        this.runOutcome.set(null);
        this.checkResult.set(null);
        this.requestError.set(null);
        this.showHint.set(false);
        this.confirmReveal.set(false);
        this.answer.set(null);
        if (id !== null && this.progress.isRevealed(db, id)) this.loadAnswer();
        history.replaceState(null, '', id === null ? '#free' : `#${id}`);
      });
    });

    // Save what's typed; the key is read untracked so switching exercise never writes into the wrong draft.
    effect(() => {
      const sql = this.sql();
      untracked(() => this.progress.saveDraft(this.db(), this.currentId(), sql));
    });

    effect(() => {
      const db = this.db();
      this.progress.setDb(db);
      this.api.schema(db).subscribe({ next: s => this.schema.set(s), error: () => this.schema.set([]) });
    });
  }

  protected select(id: number | null) {
    this.currentId.set(id);
    queueMicrotask(() => this.editor().focus());
  }

  protected step(delta: number) {
    const next = this.exercises()[this.index() + delta];
    if (next) this.select(next.id);
  }

  protected run() {
    if (this.busy()) return;
    this.start('run');
    this.checkResult.set(null);
    this.requestError.set(null);
    this.api.run(this.db(), this.sql()).subscribe({
      next: r => (this.runOutcome.set(r), this.finish()),
      error: e => this.fail(e),
    });
  }

  protected check() {
    const id = this.currentId();
    if (id === null || this.busy()) return;
    this.start('check');
    this.runOutcome.set(null);
    this.requestError.set(null);
    this.api.check(this.db(), id, this.sql()).subscribe({
      next: r => {
        this.checkResult.set(r);
        this.finish();
        if (r.correct) this.progress.markSolved(this.db(), id);
      },
      error: e => this.fail(e),
    });
  }

  protected reveal() {
    const id = this.currentId();
    if (id === null) return;
    this.confirmReveal.set(false);
    if (!this.progress.isSolved(this.db(), id)) this.progress.markRevealed(this.db(), id);
    this.loadAnswer();
  }

  protected async copyQuery() {
    const text = this.editor().selectionOrAll();
    if (!text.trim()) return this.flash('Nothing to copy');
    try {
      await navigator.clipboard.writeText(text);
      this.flash('Copied ✓');
    } catch {
      this.flash('Copy blocked by the browser. Use Ctrl+C');
    }
  }

  protected async pasteQuery() {
    try {
      const text = await navigator.clipboard.readText();
      if (text) this.editor().insert(text);
      this.flash(text ? 'Pasted ✓' : 'Clipboard is empty');
    } catch {
      this.flash('Paste blocked by the browser. Use Ctrl+V');
      this.editor().focus();
    }
  }

  private flash(message: string) {
    this.clipboardMessage.set(message);
    clearTimeout(this.flashTimer);
    this.flashTimer = setTimeout(() => this.clipboardMessage.set(null), 2000);
  }

  protected copyAnswer() {
    const a = this.answer();
    if (a) this.sql.set(a.sql);
  }

  protected insertTable(name: string) {
    this.editor().insert(`SalesLT.${name}`);
  }

  protected toggleTable(name: string) {
    this.openTables.update(s => {
      const next = new Set(s);
      next.has(name) ? next.delete(name) : next.add(name);
      return next;
    });
  }

  protected status(e: Exercise): 'solved' | 'revealed' | null {
    if (this.progress.isSolved(this.db(), e.id)) return 'solved';
    if (this.progress.isRevealed(this.db(), e.id)) return 'revealed';
    return null;
  }

  protected resetProgress() {
    if (confirm('Clear all solved marks and saved SQL drafts?')) {
      this.progress.reset();
      this.sql.set('');
    }
  }

  private loadAnswer() {
    const id = this.currentId();
    if (id !== null) this.api.answer(this.db(), id).subscribe(a => this.answer.set(a));
  }

  private start(kind: 'run' | 'check') {
    this.busy.set(kind);
    clearTimeout(this.slowTimer);
    this.slowTimer = setTimeout(() => this.slow.set(true), 3000);
  }

  private finish() {
    this.busy.set(null);
    clearTimeout(this.slowTimer);
    this.slow.set(false);
  }

  private fail(e: HttpErrorResponse) {
    this.finish();
    this.requestError.set(e.status === 0 ? 'Could not reach the API. Is it running?' : `Request failed (${e.status}): ${e.message}`);
  }
}

function initialId(): number | null {
  const hash = location.hash.slice(1);
  if (hash === 'free') return null;
  const n = Number(hash);
  return Number.isInteger(n) && n > 0 ? n : 1;
}
