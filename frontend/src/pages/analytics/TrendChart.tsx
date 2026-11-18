import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import type { DailyTicketTrend } from '../../lib/types';

export const TREND_SERIES = [
  { key: 'createdCount', label: 'Created', color: 'var(--viz-series-1)' },
  { key: 'resolvedCount', label: 'Resolved', color: 'var(--viz-series-2)' },
  { key: 'breachedCount', label: 'SLA breached', color: 'var(--viz-series-3)' },
] as const;

type SeriesKey = (typeof TREND_SERIES)[number]['key'];

const HEIGHT = 260;
const PAD = { top: 16, right: 96, bottom: 28, left: 40 };

export function formatTrendDate(date: string) {
  const d = new Date(`${date}T00:00:00Z`);
  return Number.isNaN(d.getTime()) ? date : d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' });
}

/** Rounds the y-domain up to a tidy value so gridlines land on whole numbers. */
function niceMax(v: number) {
  if (v <= 4) return 4;
  const pow = 10 ** Math.floor(Math.log10(v));
  const step = [1, 1.5, 2, 2.5, 5, 10].find((s) => s * pow * 4 >= v) ?? 10;
  return step * pow * 4;
}

/**
 * Daily ticket trends: one shared count axis, three 2px lines, a legend plus direct end labels,
 * and a crosshair tooltip (pointer and ←/→ keys). The table view lives beside it on the page.
 */
export function TrendChart({ data }: { data: DailyTicketTrend[] }) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(720);
  const [hover, setHover] = useState<number | null>(null);

  useLayoutEffect(() => {
    const el = wrapRef.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const ro = new ResizeObserver(([entry]) => {
      const w = Math.round(entry.contentRect.width);
      if (w > 0) setWidth(w);
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const innerW = Math.max(120, width - PAD.left - PAD.right);
  const innerH = HEIGHT - PAD.top - PAD.bottom;
  const maxV = niceMax(Math.max(0, ...data.flatMap((d) => TREND_SERIES.map((s) => d[s.key]))));
  const x = (i: number) => PAD.left + (data.length <= 1 ? innerW / 2 : (i / (data.length - 1)) * innerW);
  const y = (v: number) => PAD.top + innerH - (v / maxV) * innerH;
  const ticks = [0, 1, 2, 3, 4].map((t) => (maxV / 4) * t);
  const labelEvery = Math.max(1, Math.ceil(data.length / Math.max(2, Math.floor(innerW / 70))));

  const path = (key: SeriesKey) => data.map((d, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(d[key]).toFixed(1)}`).join(' ');

  // Direct labels at the line ends, nudged apart so they never overlap.
  const last = data.at(-1);
  const endLabels = last
    ? TREND_SERIES.map((s) => ({ ...s, y: y(last[s.key]) }))
        .sort((a, b) => a.y - b.y)
        .reduce<{ key: SeriesKey; label: string; color: string; y: number }[]>((acc, l) => {
          const prev = acc.at(-1);
          acc.push({ ...l, y: prev && l.y - prev.y < 14 ? prev.y + 14 : l.y });
          return acc;
        }, [])
    : [];

  const pickIndex = (clientX: number) => {
    const rect = wrapRef.current?.getBoundingClientRect();
    if (!rect || data.length === 0) return null;
    const rel = clientX - rect.left - PAD.left;
    const i = data.length <= 1 ? 0 : Math.round((rel / innerW) * (data.length - 1));
    return Math.max(0, Math.min(data.length - 1, i));
  };

  const onKey = (e: KeyboardEvent) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    e.preventDefault();
    setHover((h) => {
      const cur = h ?? (e.key === 'ArrowLeft' ? data.length : -1);
      return Math.max(0, Math.min(data.length - 1, cur + (e.key === 'ArrowRight' ? 1 : -1)));
    });
  };

  const hovered = hover != null ? data[hover] : null;
  const tipLeft = hover != null ? x(hover) : 0;

  return (
    <div className="viz-root">
      <ul className="mb-3 flex flex-wrap gap-4 text-xs text-zinc-600 dark:text-zinc-400" aria-label="Legend">
        {TREND_SERIES.map((s) => (
          <li key={s.key} className="flex items-center gap-1.5">
            <svg width="16" height="4" aria-hidden>
              <line x1="0" y1="2" x2="16" y2="2" stroke={s.color} strokeWidth="2" strokeLinecap="round" />
            </svg>
            {s.label}
          </li>
        ))}
      </ul>
      <div ref={wrapRef} className="relative" data-testid="trend-chart">
        <svg
          width="100%"
          height={HEIGHT}
          viewBox={`0 0 ${width} ${HEIGHT}`}
          role="img"
          aria-label={`Daily ticket trends over ${data.length} days: created, resolved and SLA-breached counts. Use the table view for exact values.`}
          className="block overflow-visible"
        >
          {ticks.map((t) => (
            <g key={t}>
              <line x1={PAD.left} x2={PAD.left + innerW} y1={y(t)} y2={y(t)} stroke="var(--viz-grid)" strokeWidth="1" />
              <text x={PAD.left - 8} y={y(t)} dy="0.32em" textAnchor="end" fontSize="11" fill="var(--viz-axis)" className="tabular-nums">
                {Number.isInteger(t) ? t : t.toFixed(1)}
              </text>
            </g>
          ))}
          {data.map((d, i) =>
            i % labelEvery === 0 || i === data.length - 1 ? (
              <text key={d.date} x={x(i)} y={HEIGHT - 8} textAnchor="middle" fontSize="11" fill="var(--viz-axis)">
                {formatTrendDate(d.date)}
              </text>
            ) : null,
          )}
          {TREND_SERIES.map((s) => (
            <path
              key={s.key}
              d={path(s.key)}
              fill="none"
              stroke={s.color}
              strokeWidth="2"
              strokeLinejoin="round"
              strokeLinecap="round"
              data-series={s.key}
            />
          ))}
          {data.length === 1 &&
            TREND_SERIES.map((s) => <circle key={s.key} cx={x(0)} cy={y(data[0][s.key])} r="4" fill={s.color} stroke="var(--viz-surface)" strokeWidth="2" />)}
          {endLabels.map((l) => (
            <g key={l.key}>
              <line x1={PAD.left + innerW + 8} x2={PAD.left + innerW + 18} y1={l.y} y2={l.y} stroke={l.color} strokeWidth="2" strokeLinecap="round" />
              <text x={PAD.left + innerW + 22} y={l.y} dy="0.32em" fontSize="11" fill="var(--viz-ink)">
                {l.label}
              </text>
            </g>
          ))}
          {hovered && hover != null && (
            <g pointerEvents="none">
              <line x1={x(hover)} x2={x(hover)} y1={PAD.top} y2={PAD.top + innerH} stroke="var(--viz-axis)" strokeWidth="1" strokeDasharray="3 3" />
              {TREND_SERIES.map((s) => (
                <circle key={s.key} cx={x(hover)} cy={y(hovered[s.key])} r="4" fill={s.color} stroke="var(--viz-surface)" strokeWidth="2" />
              ))}
            </g>
          )}
          <rect
            x={PAD.left - 10}
            y={PAD.top}
            width={innerW + 20}
            height={innerH}
            fill="transparent"
            tabIndex={0}
            aria-label="Chart hover area: use left and right arrow keys to read each day"
            onPointerMove={(e: PointerEvent) => setHover(pickIndex(e.clientX))}
            onPointerLeave={() => setHover(null)}
            onKeyDown={onKey}
            onBlur={() => setHover(null)}
            className="cursor-crosshair outline-none focus-visible:stroke-indigo-500"
          />
        </svg>
        {hovered && (
          <div
            role="status"
            data-testid="trend-tooltip"
            className="pointer-events-none absolute top-2 z-10 min-w-36 rounded-lg border border-zinc-200 bg-white px-3 py-2 text-xs shadow-lg dark:border-zinc-700 dark:bg-zinc-900"
            style={tipLeft > width / 2 ? { right: width - tipLeft + 12 } : { left: tipLeft + 12 }}
          >
            <p className="mb-1 font-medium text-zinc-500">{formatTrendDate(hovered.date)}</p>
            {TREND_SERIES.map((s) => (
              <p key={s.key} className="flex items-center gap-2 text-zinc-600 dark:text-zinc-400">
                <svg width="10" height="4" aria-hidden>
                  <line x1="0" y1="2" x2="10" y2="2" stroke={s.color} strokeWidth="2" strokeLinecap="round" />
                </svg>
                <strong className="tabular-nums text-zinc-900 dark:text-zinc-100">{hovered[s.key]}</strong> {s.label}
              </p>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
