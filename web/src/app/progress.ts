import { Injectable, signal } from '@angular/core';
import { DbKey } from './api.service';

type PerDb = Record<DbKey, number[]>;
interface Saved {
  solved: PerDb;
  revealed: PerDb;
  drafts: Record<string, string>;
  db: DbKey;
}

const KEY = 'sql-practice:v1';
const empty = (): PerDb => ({ sqlserver: [], mysql: [], postgres: [] });

/** Solved/revealed exercises and SQL drafts, kept in this browser only. */
@Injectable({ providedIn: 'root' })
export class Progress {
  private readonly state = signal<Saved>(load());

  readonly solved = () => this.state().solved;
  readonly revealed = () => this.state().revealed;
  readonly lastDb = () => this.state().db;

  isSolved(db: DbKey, id: number) {
    return this.state().solved[db].includes(id);
  }

  isRevealed(db: DbKey, id: number) {
    return this.state().revealed[db].includes(id);
  }

  markSolved(db: DbKey, id: number) {
    if (!this.isSolved(db, id)) this.update(s => ({ ...s, solved: { ...s.solved, [db]: [...s.solved[db], id] } }));
  }

  markRevealed(db: DbKey, id: number) {
    if (!this.isRevealed(db, id)) this.update(s => ({ ...s, revealed: { ...s.revealed, [db]: [...s.revealed[db], id] } }));
  }

  draft(db: DbKey, id: number | null) {
    return this.state().drafts[`${db}:${id ?? 'free'}`] ?? '';
  }

  saveDraft(db: DbKey, id: number | null, sql: string) {
    const key = `${db}:${id ?? 'free'}`;
    if ((this.state().drafts[key] ?? '') !== sql) this.update(s => ({ ...s, drafts: { ...s.drafts, [key]: sql } }));
  }

  setDb(db: DbKey) {
    this.update(s => ({ ...s, db }));
  }

  reset() {
    this.update(s => ({ ...s, solved: empty(), revealed: empty(), drafts: {} }));
  }

  private update(f: (s: Saved) => Saved) {
    this.state.update(f);
    try {
      localStorage.setItem(KEY, JSON.stringify(this.state()));
    } catch {
      /* storage unavailable: progress lasts for this session only */
    }
  }
}

function load(): Saved {
  const fallback: Saved = { solved: empty(), revealed: empty(), drafts: {}, db: 'sqlserver' };
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? { ...fallback, ...JSON.parse(raw) } : fallback;
  } catch {
    return fallback;
  }
}
