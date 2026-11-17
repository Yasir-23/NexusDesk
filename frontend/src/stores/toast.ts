import { create } from 'zustand';

export type ToastTone = 'success' | 'error' | 'info' | 'warning';

export interface Toast {
  id: number;
  tone: ToastTone;
  title: string;
  description?: string;
  meta?: string;
}

interface ToastState {
  toasts: Toast[];
  push: (toast: Omit<Toast, 'id'>) => void;
  dismiss: (id: number) => void;
}

let nextId = 1;
const DEDUPE_MS = 2500;
const recent = new Map<string, number>();

export const useToastStore = create<ToastState>()((set, get) => ({
  toasts: [],
  push: (toast) => {
    // Drop identical toasts fired in quick succession (e.g. parallel queries failing together).
    const key = `${toast.tone}|${toast.title}|${toast.description ?? ''}`;
    const now = Date.now();
    if ((recent.get(key) ?? 0) > now - DEDUPE_MS) return;
    recent.set(key, now);

    const id = nextId++;
    set({ toasts: [...get().toasts.slice(-4), { ...toast, id }] });
    setTimeout(() => get().dismiss(id), toast.tone === 'error' ? 7000 : 4500);
  },
  dismiss: (id) => set({ toasts: get().toasts.filter((t) => t.id !== id) }),
}));

/** Clears visible toasts and the de-dupe window (used by tests). */
export function resetToasts() {
  recent.clear();
  useToastStore.setState({ toasts: [] });
}

export const toast = {
  success: (title: string, description?: string) => useToastStore.getState().push({ tone: 'success', title, description }),
  error: (title: string, description?: string, meta?: string) =>
    useToastStore.getState().push({ tone: 'error', title, description, meta }),
  info: (title: string, description?: string) => useToastStore.getState().push({ tone: 'info', title, description }),
  warning: (title: string, description?: string) => useToastStore.getState().push({ tone: 'warning', title, description }),
};
