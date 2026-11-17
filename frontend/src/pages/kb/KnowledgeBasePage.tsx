import { BookOpen, Eye, FileText, Plus, Search, ThumbsUp } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { PageHeader } from '../../components/layout/AppShell';
import { Markdown } from '../../components/domain/Markdown';
import { KbStatusBadge } from '../../components/domain/TicketBadges';
import { Badge, Button, Card, Dialog, EmptyState, ErrorPanel, Input, Skeleton, Spinner } from '../../components/ui';
import { cn } from '../../lib/cn';
import { formatDate } from '../../lib/format';
import { useKbArticle, useKbArticles, useKbCategories, useKbSearch } from '../../lib/kb';
import type { KbArticle, KbSearchResult } from '../../lib/types';
import { useAuthStore } from '../../stores/auth';
import { NewArticleDialog } from './NewArticleDialog';

function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

/** `relevanceScore` is a 0–1 hybrid score; show it as a percentage badge. */
export function relevanceLabel(score: number) {
  return score <= 1 ? `${Math.round(score * 100)}% match` : `score ${score.toFixed(2)}`;
}

function relevanceTone(score: number) {
  return score >= 0.75 ? 'green' : score >= 0.4 ? 'amber' : 'neutral';
}

export function KnowledgeBasePage() {
  const role = useAuthStore((s) => s.user!.role);
  const isStaff = role === 'Agent' || role === 'TenantAdmin';
  const [params, setParams] = useSearchParams();
  const articleParam = params.get('article');

  const [term, setTerm] = useState('');
  const debounced = useDebounced(term);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [newOpen, setNewOpen] = useState(false);

  const categories = useKbCategories();
  const searching = debounced.trim().length >= 2;
  const search = useKbSearch(debounced, 10);
  const browse = useKbArticles({ categoryId: categoryId ?? undefined, page: 1, pageSize: 50 }, !searching);

  const categoryName = categories.data?.find((c) => c.id === categoryId)?.name;
  const results = useMemo(
    () => (search.data ?? []).filter((r) => !categoryName || r.categoryName === categoryName),
    [search.data, categoryName],
  );

  const openArticle = (idOrSlug: string | null) =>
    setParams((p) => {
      const next = new URLSearchParams(p);
      if (idOrSlug) next.set('article', idOrSlug);
      else next.delete('article');
      return next;
    });

  return (
    <>
      <PageHeader
        title={isStaff ? 'Knowledge Base' : 'Help Center'}
        description="Hybrid full-text + semantic deflection search across this tenant's articles."
        actions={
          isStaff && (
            <Button icon={<Plus className="h-4 w-4" />} onClick={() => setNewOpen(true)}>
              New article
            </Button>
          )
        }
      />

      <div className="relative mb-4">
        <Search className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-zinc-400" aria-hidden />
        <Input
          aria-label="Search articles"
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Describe your problem, e.g. “cannot login to sso”"
          className="h-11 pl-10 text-base"
        />
        {search.isFetching && searching && <Spinner className="absolute right-3.5 top-1/2 -translate-y-1/2" />}
      </div>

      <div role="group" aria-label="Filter by category" className="mb-4 flex flex-wrap gap-1.5">
        <CategoryChip active={!categoryId} onClick={() => setCategoryId(null)}>
          All
        </CategoryChip>
        {categories.data?.map((c) => (
          <CategoryChip key={c.id} active={categoryId === c.id} onClick={() => setCategoryId(c.id)}>
            {c.name}
            <span className="ml-1 text-[10px] opacity-60">{c.publishedArticlesCount}</span>
          </CategoryChip>
        ))}
        {categories.isLoading && <Skeleton className="h-7 w-40" />}
      </div>

      {searching ? (
        <SearchResults
          term={debounced.trim()}
          results={results}
          isLoading={search.isLoading}
          error={search.error}
          onOpen={(r) => openArticle(r.slug)}
        />
      ) : (
        <ArticleGrid
          articles={browse.data?.items}
          isLoading={browse.isLoading}
          error={browse.error}
          isStaff={isStaff}
          onOpen={(a) => openArticle(a.slug)}
        />
      )}

      <ArticleReader idOrSlug={articleParam} onClose={() => openArticle(null)} isStaff={isStaff} />

      {isStaff && (
        <NewArticleDialog
          open={newOpen}
          onClose={() => setNewOpen(false)}
          categories={categories.data ?? []}
          onCreated={(a) => {
            setNewOpen(false);
            openArticle(a.slug);
          }}
        />
      )}
    </>
  );
}

function CategoryChip({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        'rounded-full px-3 py-1 text-xs font-medium ring-1 ring-inset transition-colors',
        active
          ? 'bg-indigo-600 text-white ring-indigo-600'
          : 'bg-white text-zinc-700 ring-zinc-200 hover:bg-zinc-50 dark:bg-zinc-900 dark:text-zinc-300 dark:ring-zinc-700 dark:hover:bg-zinc-800',
      )}
    >
      {children}
    </button>
  );
}

function SearchResults({
  term,
  results,
  isLoading,
  error,
  onOpen,
}: {
  term: string;
  results: KbSearchResult[];
  isLoading: boolean;
  error: unknown;
  onOpen: (r: KbSearchResult) => void;
}) {
  if (error) return <ErrorPanel error={error} />;
  if (isLoading) return <Skeleton className="h-40 w-full" />;
  if (results.length === 0) {
    return (
      <Card>
        <EmptyState icon={<Search className="h-5 w-5" />} title={`No articles match “${term}”`} description="Try different keywords or another category." />
      </Card>
    );
  }
  return (
    <Card>
      <p className="border-b border-zinc-100 px-5 py-2.5 text-xs text-zinc-500 dark:border-zinc-800">
        {results.length} result{results.length === 1 ? '' : 's'} for “{term}”
      </p>
      <ul className="divide-y divide-zinc-100 dark:divide-zinc-800" aria-label="Search results">
        {results.map((r) => (
          <li key={r.id} data-testid={`kb-result-${r.slug}`}>
            <button type="button" onClick={() => onOpen(r)} className="block w-full px-5 py-3.5 text-left hover:bg-zinc-50 dark:hover:bg-zinc-800/40">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium text-zinc-900 dark:text-zinc-100">{r.title}</span>
                <Badge tone={relevanceTone(r.relevanceScore)}>{relevanceLabel(r.relevanceScore)}</Badge>
              </div>
              {r.summary && <p className="mt-0.5 text-sm text-zinc-600 dark:text-zinc-400">{r.summary}</p>}
              <p className="mt-1 flex items-center gap-3 text-[11px] text-zinc-500">
                <span>{r.categoryName}</span>
                <span className="inline-flex items-center gap-1">
                  <ThumbsUp className="h-3 w-3" aria-hidden /> {r.helpfulCount} found helpful
                </span>
              </p>
            </button>
          </li>
        ))}
      </ul>
    </Card>
  );
}

function ArticleGrid({
  articles,
  isLoading,
  error,
  isStaff,
  onOpen,
}: {
  articles: KbArticle[] | undefined;
  isLoading: boolean;
  error: unknown;
  isStaff: boolean;
  onOpen: (a: KbArticle) => void;
}) {
  if (error) return <ErrorPanel error={error} />;
  if (isLoading || !articles) {
    return (
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 6 }, (_, i) => (
          <Skeleton key={i} className="h-28 w-full" />
        ))}
      </div>
    );
  }
  if (articles.length === 0) {
    return (
      <Card>
        <EmptyState icon={<BookOpen className="h-5 w-5" />} title="No articles in this category yet" />
      </Card>
    );
  }
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {articles.map((a) => (
        <button
          key={a.id}
          type="button"
          data-testid={`kb-article-${a.slug}`}
          onClick={() => onOpen(a)}
          className="rounded-xl border border-zinc-200 bg-white p-4 text-left shadow-sm transition hover:border-indigo-300 hover:shadow dark:border-zinc-800 dark:bg-zinc-900 dark:hover:border-indigo-500/50"
        >
          <div className="flex items-start justify-between gap-2">
            <FileText className="mt-0.5 h-4 w-4 shrink-0 text-indigo-500" aria-hidden />
            {isStaff && a.status !== 'Published' && <KbStatusBadge status={a.status} />}
          </div>
          <p className="mt-2 line-clamp-2 font-medium text-zinc-900 dark:text-zinc-100">{a.title}</p>
          {a.summary && <p className="mt-1 line-clamp-2 text-xs text-zinc-500">{a.summary}</p>}
          <p className="mt-2 text-[11px] text-zinc-400">
            {a.categoryName ?? 'Uncategorised'} · {formatDate(a.publishedAtUtc ?? a.updatedAtUtc)}
          </p>
        </button>
      ))}
    </div>
  );
}

/** `GET /api/v1/kb/articles/{idOrSlug}` rendered in a side drawer. */
function ArticleReader({ idOrSlug, onClose, isStaff }: { idOrSlug: string | null; onClose: () => void; isStaff: boolean }) {
  const { data, isLoading, error } = useKbArticle(idOrSlug);
  return (
    <Dialog open={Boolean(idOrSlug)} onClose={onClose} variant="side" title={data?.title ?? 'Article'} description={data?.summary ?? undefined}>
      {error ? (
        <ErrorPanel error={error} />
      ) : isLoading || !data ? (
        <div className="space-y-3">
          <Skeleton className="h-5 w-2/3" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : (
        <article data-testid="kb-reader" className="space-y-4">
          <div className="flex flex-wrap items-center gap-1.5">
            {data.categoryName && <Badge tone="indigo">{data.categoryName}</Badge>}
            {isStaff && <KbStatusBadge status={data.status} />}
            {data.tags.map((t) => (
              <Badge key={t}>#{t}</Badge>
            ))}
          </div>
          <Markdown source={data.bodyMarkdown} />
          <p className="flex flex-wrap items-center gap-3 border-t border-zinc-100 pt-3 text-[11px] text-zinc-500 dark:border-zinc-800">
            <span>Updated {formatDate(data.updatedAtUtc)}</span>
            <span className="inline-flex items-center gap-1">
              <Eye className="h-3 w-3" aria-hidden /> {data.viewCount} views
            </span>
            <span className="inline-flex items-center gap-1">
              <ThumbsUp className="h-3 w-3" aria-hidden /> {data.helpfulCount} helpful
            </span>
            <span className="font-mono">/{data.slug}</span>
          </p>
        </article>
      )}
    </Dialog>
  );
}
