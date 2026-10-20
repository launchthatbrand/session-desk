import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService, SearchService } from '../core/auth.service';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html',
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  readonly search = inject(SearchService);
  private readonly router = inject(Router);
  readonly collapsed = signal(false);

  onSearch(value: string): void {
    this.search.query.set(value);
  }

  submitSearch(event: Event): void {
    event.preventDefault();
    const term = this.search.query().trim();
    void this.router.navigate(['/search'], { queryParams: term ? { q: term } : {} });
  }

  onCommittee(value: string): void {
    this.auth.selectCommittee(value);
  }

  signOut(): void {
    this.auth.clear();
    void this.router.navigateByUrl('/login');
  }

  initial(): string {
    return (this.auth.user()?.displayName ?? '?').slice(0, 1);
  }
}
