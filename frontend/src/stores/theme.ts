import { create } from 'zustand';

type Theme = 'light' | 'dark';
const KEY = 'nexusdesk-theme';

function initialTheme(): Theme {
  if (typeof document !== 'undefined' && document.documentElement.classList.contains('dark')) return 'dark';
  return 'light';
}

export const useThemeStore = create<{ theme: Theme; toggle: () => void }>()((set, get) => ({
  theme: initialTheme(),
  toggle: () => {
    const theme: Theme = get().theme === 'dark' ? 'light' : 'dark';
    document.documentElement.classList.toggle('dark', theme === 'dark');
    try {
      localStorage.setItem(KEY, theme);
    } catch {
      /* storage unavailable - theme just won't persist */
    }
    set({ theme });
  },
}));
