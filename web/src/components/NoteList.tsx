import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import { useVirtualizer } from '@tanstack/react-virtual';
import { FileText, MoreHorizontal, Pencil, Plus, Search, Trash2, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { NoteSummary } from '../api/types';
import { formatRelativeTime } from '../lib/utils';
import { ConfirmDialog, PromptDialog } from './Dialogs';

interface Props {
  notes: NoteSummary[] | undefined;
  loading: boolean;
  activeNoteId: string | null;
  search: string;
  onSearch: (value: string) => void;
  onOpen: (noteId: string) => void;
  onCreate: () => Promise<void>;
  onRename: (note: NoteSummary, title: string) => Promise<void>;
  onDelete: (note: NoteSummary) => Promise<void>;
}

const ROW_HEIGHT = 86;

export function NoteList({
  notes,
  loading,
  activeNoteId,
  search,
  onSearch,
  onOpen,
  onCreate,
  onRename,
  onDelete,
}: Props) {
  const parentRef = useRef<HTMLDivElement>(null);
  const [renaming, setRenaming] = useState<NoteSummary | null>(null);
  const [deleting, setDeleting] = useState<NoteSummary | null>(null);

  const virtualizer = useVirtualizer({
    count: notes?.length ?? 0,
    getScrollElement: () => parentRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
  });

  useEffect(() => {
    const index = notes?.findIndex((note) => note.id === activeNoteId) ?? -1;
    if (index >= 0) {
      virtualizer.scrollToIndex(index, { align: 'auto' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeNoteId, notes?.length]);

  return (
    <section className="flex h-full w-full flex-col overflow-hidden border-r border-[var(--border)] bg-[var(--surface)]">
      <div className="space-y-2.5 px-3 pb-2 pt-3.5">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--text)]">
            笔记
            <span className="ml-1.5 font-normal text-[var(--text-faint)]">
              {notes?.length ?? 0}
            </span>
          </h2>
          <button
            type="button"
            title="新建笔记 (⌘N)"
            onClick={() => void onCreate()}
            className="grid size-7 place-items-center rounded-lg bg-[var(--accent)] text-white shadow-sm hover:brightness-105"
          >
            <Plus size={16} />
          </button>
        </div>

        <div className="relative">
          <Search
            size={14}
            className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--text-faint)]"
          />
          <input
            value={search}
            onChange={(event) => onSearch(event.target.value)}
            placeholder="搜索标题或内容"
            className="w-full rounded-lg border border-transparent bg-[var(--surface-sunken)] py-1.5 pl-8 pr-7 text-[13px] outline-none placeholder:text-[var(--text-faint)] focus:border-[var(--border-strong)]"
          />
          {search && (
            <button
              type="button"
              onClick={() => onSearch('')}
              className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-0.5 text-[var(--text-faint)] hover:text-[var(--text)]"
              aria-label="清除搜索"
            >
              <X size={13} />
            </button>
          )}
        </div>
      </div>

      <div ref={parentRef} className="flex-1 overflow-y-auto px-2 pb-3">
        {loading && !notes ? (
          <div className="space-y-2 px-1 pt-2">
            {[0, 1, 2, 3].map((key) => (
              <div
                key={key}
                className="h-[74px] animate-pulse rounded-xl bg-[var(--surface-sunken)]"
              />
            ))}
          </div>
        ) : (notes?.length ?? 0) === 0 ? (
          <div className="px-4 py-10 text-center text-[13px] leading-relaxed text-[var(--text-faint)]">
            {search ? '没有匹配的笔记。' : '这里还没有笔记，点击 + 新建。'}
          </div>
        ) : (
          <div
            className="relative w-full"
            style={{ height: `${virtualizer.getTotalSize()}px` }}
          >
            {virtualizer.getVirtualItems().map((item) => {
              const note = notes![item.index];
              const active = note.id === activeNoteId;
              return (
                <div
                  key={note.id}
                  data-index={item.index}
                  ref={virtualizer.measureElement}
                  className="absolute inset-x-0 top-0 px-1 py-1"
                  style={{ transform: `translateY(${item.start}px)` }}
                >
                  <div
                    role="button"
                    tabIndex={0}
                    onClick={() => onOpen(note.id)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        onOpen(note.id);
                      }
                    }}
                    className={`group relative cursor-pointer rounded-xl border px-3 py-2.5 outline-none transition-colors ${
                      active
                        ? 'border-[var(--accent-border)] bg-[var(--accent-soft)]'
                        : 'border-transparent hover:bg-[var(--surface-hover)]'
                    }`}
                  >
                    <div className="flex items-start gap-2">
                      <span
                        className={`mt-0.5 flex size-3.5 shrink-0 items-center justify-center ${
                          active ? 'text-[var(--accent-strong)]' : 'text-[var(--text-faint)]'
                        }`}
                      >
                        {active ? (
                          <span className="block size-1.5 rounded-full bg-[var(--accent)]" />
                        ) : (
                          <FileText size={13} />
                        )}
                      </span>
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-[13.5px] font-medium leading-5">
                          {note.title}
                        </div>
                        <p className="mt-0.5 line-clamp-2 text-[12px] leading-[1.5] text-[var(--text-muted)]">
                          {note.excerpt || '空白笔记'}
                        </p>
                        <div className="mt-1 flex items-center gap-1.5 text-[11px] text-[var(--text-faint)]">
                          <span>{formatRelativeTime(note.modifiedAt)}</span>
                          {note.projectName && note.projectName !== '未分类' && (
                            <>
                              <span>·</span>
                              <span className="truncate">{note.projectName}</span>
                            </>
                          )}
                        </div>
                      </div>
                    </div>

                    <DropdownMenu.Root>
                      <DropdownMenu.Trigger asChild>
                        <button
                          type="button"
                          aria-label={`${note.title} 的操作`}
                          onClick={(event) => event.stopPropagation()}
                          className="absolute right-2 top-2 hidden rounded p-1 text-[var(--text-faint)] hover:bg-[var(--surface-raised)] hover:text-[var(--text)] group-hover:block data-[state=open]:block"
                        >
                          <MoreHorizontal size={14} />
                        </button>
                      </DropdownMenu.Trigger>
                      <DropdownMenu.Portal>
                        <DropdownMenu.Content
                          align="end"
                          sideOffset={4}
                          className="z-50 min-w-36 rounded-lg border border-[var(--border)] bg-[var(--surface-raised)] p-1 text-[13px] shadow-xl"
                        >
                          <DropdownMenu.Item
                            onSelect={() => setRenaming(note)}
                            className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
                          >
                            <Pencil size={13} /> 重命名
                          </DropdownMenu.Item>
                          <DropdownMenu.Item
                            onSelect={() => setDeleting(note)}
                            className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-[var(--danger)] outline-none data-[highlighted]:bg-[var(--danger-soft)]"
                          >
                            <Trash2 size={13} /> 删除
                          </DropdownMenu.Item>
                        </DropdownMenu.Content>
                      </DropdownMenu.Portal>
                    </DropdownMenu.Root>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      <PromptDialog
        open={renaming !== null}
        title="重命名笔记"
        label="标题"
        initialValue={renaming?.title ?? ''}
        confirmText="保存"
        onOpenChange={(open) => !open && setRenaming(null)}
        onSubmit={async (value) => {
          if (renaming) {
            await onRename(renaming, value);
          }
        }}
      />

      <ConfirmDialog
        open={deleting !== null}
        title={`删除「${deleting?.title ?? ''}」？`}
        description="笔记会移入回收站，可以随时恢复。"
        confirmText="移入回收站"
        onOpenChange={(open) => !open && setDeleting(null)}
        onConfirm={async () => {
          if (deleting) {
            await onDelete(deleting);
          }
        }}
      />
    </section>
  );
}
