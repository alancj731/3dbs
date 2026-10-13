import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export type DbKey = 'sqlserver' | 'mysql' | 'postgres';

export const DATABASES: { key: DbKey; label: string }[] = [
  { key: 'sqlserver', label: 'SQL Server' },
  { key: 'mysql', label: 'MySQL' },
  { key: 'postgres', label: 'PostgreSQL' },
];

export interface Exercise {
  id: number;
  category: string;
  difficulty: 'easy' | 'medium' | 'hard';
  title: string;
  prompt: string;
  hint: string | null;
  ordered: boolean;
  kind: 'read' | 'write';
}

export interface QueryResult {
  columns: string[];
  rows: unknown[][];
  rowsAffected: number;
  truncated: boolean;
  elapsedMs: number;
}

export interface RunOutcome {
  result: QueryResult | null;
  check: QueryResult | null;
  error: string | null;
}

export interface CheckResult {
  correct: boolean;
  message: string;
  yours: QueryResult | null;
  expected: QueryResult | null;
  missing: unknown[][];
  extra: unknown[][];
  error: string | null;
}

export interface Answer {
  sql: string;
  check: string | null;
}

export interface TableInfo {
  name: string;
  columns: { name: string; type: string; nullable: boolean }[];
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  exercises(): Observable<Exercise[]> {
    return this.http.get<Exercise[]>(`${environment.apiBase}/api/exercises`);
  }

  schema(db: DbKey): Observable<TableInfo[]> {
    return this.http.get<TableInfo[]>(`${environment.apiBase}/api/${db}/schema`);
  }

  run(db: DbKey, sql: string): Observable<RunOutcome> {
    return this.http.post<RunOutcome>(`${environment.apiBase}/api/${db}/run`, { sql });
  }

  check(db: DbKey, id: number, sql: string): Observable<CheckResult> {
    return this.http.post<CheckResult>(`${environment.apiBase}/api/${db}/exercises/${id}/check`, { sql });
  }

  answer(db: DbKey, id: number): Observable<Answer> {
    return this.http.get<Answer>(`${environment.apiBase}/api/${db}/exercises/${id}/answer`);
  }
}
