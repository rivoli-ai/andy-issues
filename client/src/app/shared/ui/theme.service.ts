import { Injectable } from '@angular/core';
import { setTheme, type AndyTheme } from '@andy-ui/angular';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  static readonly storageKey = 'andy-issues.theme';
  constructor() {
    let saved: string | null = null;
    try { saved = localStorage.getItem(ThemeService.storageKey); } catch { /* storage can be disabled */ }
    const theme = saved === 'light' || saved === 'dark' ? saved
      : window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    setTheme(theme);
  }
  select(theme: AndyTheme): void {
    setTheme(theme);
    try { localStorage.setItem(ThemeService.storageKey, theme); } catch { /* theme still applies */ }
  }
}
