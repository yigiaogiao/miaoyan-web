import * as Dialog from '@radix-ui/react-dialog';
import { useQuery } from '@tanstack/react-query';
import { CornerDownLeft, Loader2, Search } from 'lucide-react';
import {
  useEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type ReactNode,
} from 'react';
import { api } from '../api/client';
import type { NoteSummary } from '../api/types';
import { formatRelativeTime } from '../lib/utils';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onOpenNote: (id: string) => void;
  recent: NoteSummary[] | undefined;
}

function highlight(text: string, query: string): ReactNode {
  const terms = query
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .map((term) => term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  if (terms.length === 0) {
    return text;
  }

  const parts = text.split(new RegExp(`(${terms.join('|')})`, 'gi'));
  return parts.map((part, index) =>
    terms.some((term) => part.toLowerCase() === term.toLowerCase()) ? (
      <mark key={index} className="rounded bg-[var(--warning-soft)] px-0.5 text-[var(--text)]">
        {part}
      </mark>
    ) : (
      <span key={index}>{part}</span>
    ),
  );
}

export function CommandPalette({ open, onOpenChange, onOpenNote, recent }: Props) {
  const [raw, setRaw] = useState('');
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(raw.trim()), 150);
    return () => window.clearTimeout(timer);
  }, [raw]);

  useEffect(() => {
    if (!open) {
      setRaw('');
      setQuery('');
      setActive(0);
    }
  }, [open]);

  const searchQuery = useQuery({
    queryKey: ['search', query],
    queryFn: () => api.search(query, undefined, 40),
    enabled: open && query.length > 0,
    placeholderData: (previous) => previous,
  });

  const results = searchQuery.data ?? [];
  const items = query
    ? results.map((hit) => ({
        id: hit.id,
        title: hit.title,
        projectName: hit.projectName,
        snippet: hit.snippet,
        modifiedAt: hit.modifiedAt,
      }))
    : (recent ?? []).slice(0, 8).map((note) => ({
        id: note.id,
        title: note.title,
        projectName: note.projectName,
        snippet: note.excerpt,
        modifiedAt: note.modifiedAt,
      }));

  useEffect(() => {
    setActive(0);
  }, [query, results.length]);

  useEffect(() => {
    const node = listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`);
    node?.scrollIntoView({ block: 'nearest' });
  }, [active]);

  function choose(id: string) {
    onOpenNote(id);
    onOpenChange(false);
  }

  function onKeyDown(event: ReactKeyboardEvent) {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActive((value) => (items.length === 0 ? 0 : (value + 1) % items.length));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActive((value) => (items.length === 0 ? 0 : (value - 1 + items.length) % items.length));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const item = items[active];
      if (item) {
        choose(item.id);
      }
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/25 backdrop-blur-[2px]" />
        <Dialog.Content className="fixed left-1/2 top-[12vh] z-50 w-[min(94vw,40rem)] -translate-x-1/2 overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-raised)] shadow-2xl outline-none">
          <Dialog.Title className="sr-only">搜索笔记</Dialog.Title>
          <div className="flex items-center gap-2.5 border-b border-[var(--border)] px-4">
            <Search size={17} className="shrink-0 text-[var(--text-faint)]" />
            <input
              autoFocus
              value={raw}
              onChange={(event) => setRaw(event.target.value)}
              onKeyDown={onKeyDown}
              placeholder="搜索标题或正文…"
              className="h-12 flex-1 bg-transparent text-sm outline-none placeholder:text-[var(--text-faint)]"
            />
            {searchQuery.isFetching && (
              <Loader2 size={14} className="animate-spin text-[var(--text-faint)]" />
            )}
          </div>

          <div ref={listRef} className="max-h-[min(60vh,26rem)] overflow-y-auto p-1.5">
            {!query && (
              <p className="px-3 pb-1 pt-2 text-[10px] font-medium uppercase tracking-wider text-[var(--text-faint)]">
                最近笔记
              </p>
            )}
            {items.length === 0 ? (
              <p className="px-4 py-10 text-center text-sm text-[var(--text-faint)]">
                {query ? `没有找到「${query}」` : '还没有笔记'}
              </p>
            ) : (
              items.map((item, index) => (
                <button
                  key={item.id}
                  type="button"
                  data-index={index}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => choose(item.id)}
                  className={`mb-0.5 flex w-full items-start gap-2.5 rounded-lg px-3 py-2.5 text-left ${
                    index === active ? 'bg-[var(--accent-soft)]' : ''
                  }`}
                >
                  <span className="mt-0.5 grid size-6 shrink-0 place-items-center rounded-md bg-[var(--surface-sunken)] text-[var(--text-faint)]">
                    <Search size={12} />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="flex items-baseline gap-2">
                      <span className="truncate text-[13.5px] font-medium">
                        {highlight(item.title, query)}
                      </span>
                      <span className="shrink-0 text-[10.5px] text-[var(--text-faint)]">
                        {item.projectName}
                      </span>
                      <span className="flex-1" />
                      <span className="shrink-0 text-[10.5px] text-[var(--text-faint)]">
                        {formatRelativeTime(item.modifiedAt)}
                      </span>
                    </span>
                    {item.snippet && (
                      <span className="mt-0.5 line-clamp-1 text-[11.5px] text-[var(--text-muted)]">
                        {highlight(item.snippet, query)}
                      </span>
                    )}
                  </span>
                </button>
              ))
            )}
          </div>

          <div className="flex items-center gap-3 border-t border-[var(--border)] px-4 py-2 text-[10.5px] text-[var(--text-faint)]">
            <span>↑↓ 选择</span>
            <span className="flex items-center gap-1">
              <CornerDownLeft size={11} /> 打开
            </span>
            <span>Esc 关闭</span>
            <span className="flex-1" />
            <span>⌘K</span>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
