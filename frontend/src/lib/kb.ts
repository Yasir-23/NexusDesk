import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './api';
import { useSessionScope, type SessionScope } from './queries';
import type { CreateKbArticleRequest, KbArticle, KbCategory, KbSearchResult, PagedKbArticlesResponse } from './types';

export interface KbArticleListQuery {
  categoryId?: string;
  page: number;
  pageSize: number;
}

export const kbKeys = {
  root: ['kb'] as const,
  categories: (scope: SessionScope) => ['kb', 'categories', scope] as const,
  articles: (q: KbArticleListQuery, scope: SessionScope) => ['kb', 'articles', q, scope] as const,
  article: (idOrSlug: string, scope: SessionScope) => ['kb', 'article', idOrSlug, scope] as const,
  search: (q: string, limit: number, scope: SessionScope) => ['kb', 'search', q, limit, scope] as const,
};

export function useKbCategories() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: kbKeys.categories(scope),
    queryFn: ({ signal }) => api.get<KbCategory[]>('/api/v1/kb/categories', { signal }),
    enabled: Boolean(scope.tenant),
    staleTime: 60_000,
  });
}

/** Browse list (no search term). Staff also receive drafts; customers only published articles. */
export function useKbArticles(q: KbArticleListQuery, enabled = true) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: kbKeys.articles(q, scope),
    queryFn: ({ signal }) =>
      api.get<PagedKbArticlesResponse>('/api/v1/kb/articles', {
        query: { categoryId: q.categoryId, page: q.page, pageSize: q.pageSize },
        signal,
      }),
    enabled: enabled && Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}

/** `GET /api/v1/kb/articles/{idOrSlug}` — accepts a GUID or a slug. */
export function useKbArticle(idOrSlug: string | null) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: kbKeys.article(idOrSlug ?? '', scope),
    queryFn: ({ signal }) => api.get<KbArticle>(`/api/v1/kb/articles/${encodeURIComponent(idOrSlug!)}`, { signal }),
    enabled: Boolean(idOrSlug && scope.tenant),
  });
}

/** Deflection search (`GET /api/v1/kb/search?q=&limit=`). Pass an already-debounced term. */
export function useKbSearch(term: string, limit = 10) {
  const scope = useSessionScope();
  const q = term.trim();
  return useQuery({
    queryKey: kbKeys.search(q, limit, scope),
    queryFn: ({ signal }) => api.get<KbSearchResult[]>('/api/v1/kb/search', { query: { q, limit }, signal }),
    enabled: q.length >= 2 && Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}

export function useCreateKbArticle() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateKbArticleRequest) => api.post<KbArticle>('/api/v1/kb/articles', body, { silent: true }),
    onSuccess: () => qc.invalidateQueries({ queryKey: kbKeys.root }),
  });
}

/** URL-safe slug from a title (`Configuring SSO with Okta` → `configuring-sso-with-okta`). */
export function slugify(title: string): string {
  return title
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 120);
}
