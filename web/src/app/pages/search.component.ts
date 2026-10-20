import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService, SearchBill, SearchHit } from '../core/api.service';
import { AuthService, SearchService } from '../core/auth.service';

@Component({
  selector: 'app-search',
  templateUrl: './search.component.html',
})
export class SearchComponent {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  private readonly search = inject(SearchService);
  readonly hits = signal<SearchHit[]>([]);
  readonly reading = signal<SearchBill | null>(null);
  readonly unavailable = signal(false);
  readonly term = signal('');

  constructor() {
    this.route.queryParamMap.subscribe((params) => {
      const term = params.get('q') ?? '';
      this.term.set(term);
      this.search.query.set(term);
      this.reading.set(null);
      if (!term.trim()) {
        this.hits.set([]);
        this.unavailable.set(false);
        return;
      }
      this.api.search(term).subscribe({
        next: (hits) => {
          this.hits.set(hits);
          this.unavailable.set(false);
        },
        error: () => {
          this.hits.set([]);
          this.unavailable.set(true);
        },
      });
    });
  }

  canOpen(hit: SearchHit): boolean {
    return this.auth.committees().some((committee) => committee.id === hit.committeeId);
  }

  open(hit: SearchHit): void {
    if (!this.canOpen(hit)) {
      this.api.searchBill(hit.billId).subscribe((bill) => this.reading.set(bill));
      return;
    }

    this.auth.selectCommittee(hit.committeeId);
    void this.router.navigate(['/bills'], { queryParams: { bill: hit.billId } });
  }

  closeReading(): void {
    this.reading.set(null);
  }

  snippetParts(snippet: string): { text: string; mark: boolean }[] {
    const parts: { text: string; mark: boolean }[] = [];
    const pattern = /<em>([\s\S]*?)<\/em>/gi;
    let last = 0;
    for (const match of snippet.matchAll(pattern)) {
      const index = match.index ?? 0;
      if (index > last) {
        parts.push({ text: snippet.slice(last, index), mark: false });
      }
      parts.push({ text: match[1] ?? '', mark: true });
      last = index + match[0].length;
    }
    if (last < snippet.length) {
      parts.push({ text: snippet.slice(last), mark: false });
    }
    return parts;
  }
}
