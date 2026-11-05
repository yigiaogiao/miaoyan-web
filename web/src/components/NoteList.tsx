import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import { useVirtualizer } from '@tanstack/react-virtual';
import {
  ArrowUpDown,
  Check,
  Copy,
  FileText,
  MoreHorizontal,
  Pencil,
  Pin,
  PinOff,
  Plus,
  Search,
  Trash2,
  X,
} from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { NoteSort, NoteSummary } from '../api/types';
import { useI18n } from '../lib/i18n';
import { formatRelativeTime } from '../lib/utils';
import { ConfirmDialog, PromptDialog } from './Dialogs';

interface Props {
  notes: NoteSummary[] | undefined;
  loading: boolean;
  activeNoteId: string | null;
  search: string;
  sort: NoteSort;
  onSearch: (value: string) => void;
  onSort: (sort: NoteSort) => void;
  onOpen: (noteId: string) => void;
  onCreate: () => Promise<void>;
  onRename: (note: NoteSummary, title: string) => Promise<void>;
  onDelete: (note: NoteSummary) => Promise<void>;
  onPin: (note: NoteSummary, pinned: boolean) => Promise<void>;
  onDuplicate: (note: NoteSummary) => Promise<void>;
}

const SORT_KEYS: Record<NoteSort, string> = {
  modified: 'notes.sortModified',
  created: 'notes.sortCreated',
  title: 'notes.sortTitle',
};

const SORT_ITEMS: NoteSort[] = ['modified', 'created', 'title'];

const ROW_HEIGHT = 86;

export function NoteList({
  notes,
  loading,
  activeNoteId,
  search,
  sort,
  onSearch,
  onSort,
  onOpen,
  onCreate,
  onRename,
  onDelete,
  onPin,
  onDuplicate,
}: Props) {
  const { t } = useI18n();
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
            {t('notes.title')}
            <span className="ml-1.5 font-normal text-[var(--text-faint)]">
              {notes?.length ?? 0}
            </span>
          </h2>
          <div className="flex items-center gap-0.5">
            <DropdownMenu.Root>
              <DropdownMenu.Trigger asChild>
                <button
                  type="button"
                  title={t('notes.sort', { name: t(SORT_KEYS[sort]) })}
                  className="grid size-7 place-items-center rounded-lg text-[var(--text-faint)] hover:bg-[var(--surface-hover)] hover:text-[var(--text)]"
                >
                  <ArrowUpDown size={14} />
                </button>
              </DropdownMenu.Trigger>
              <DropdownMenu.Portal>
                <DropdownMenu.Content
                  align="end"
                  sideOffset={4}
                  className="z-50 min-w-36 rounded-lg border border-[var(--border)] bg-[var(--surface-raised)] p-1 text-[13px] shadow-xl"
                >
                  {SORT_ITEMS.map((item) => (
                    <DropdownMenu.Item
                      key={item}
                      onSelect={() => onSort(item)}
                      className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
                    >
                      <span className="flex-1">{t(SORT_KEYS[item])}</span>
                      {sort === item && (
                        <Check size={13} className="text-[var(--accent)]" />
                      )}
                    </DropdownMenu.Item>
                  ))}
                </DropdownMenu.Content>
              </DropdownMenu.Portal>
            </DropdownMenu.Root>
            <button
              type="button"
              title={t('notes.new')}
              onClick={() => void onCreate()}
              className="grid size-7 place-items-center rounded-lg bg-[var(--accent)] text-white shadow-sm hover:brightness-105"
            >
              <Plus size={16} />
            </button>
          </div>
        </div>

        <div className="relative">
          <Search
            size={14}
            className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--text-faint)]"
          />
          <input
            value={search}
            onChange={(event) => onSearch(event.target.value)}
            placeholder={t('notes.search')}
            className="w-full rounded-lg border border-transparent bg-[var(--surface-sunken)] py-1.5 pl-8 pr-7 text-[13px] outline-none placeholder:text-[var(--text-faint)] focus:border-[var(--border-strong)]"
          />
          {search && (
            <button
              type="button"
              onClick={() => onSearch('')}
              className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-0.5 text-[var(--text-faint)] hover:text-[var(--text)]"
              aria-label={t('notes.clearSearch')}
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
            {search ? t('notes.emptySearch') : t('notes.empty')}
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
                    draggable
                    title={t('notes.moveHint')}
                    onDragStart={(event) => {
                      event.dataTransfer.setData('application/x-miaoyan-note', note.id);
                      event.dataTransfer.effectAllowed = 'move';
                    }}
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
                        <div className="flex items-center gap-1 text-[13.5px] font-medium leading-5">
                          {note.pinned && (
                            <Pin size={11} className="shrink-0 text-[var(--accent)]" fill="currentColor" />
                          )}
                          <span className="truncate">{note.title}</span>
                        </div>
                        <p className="mt-0.5 line-clamp-2 text-[12px] leading-[1.5] text-[var(--text-muted)]">
                          {note.excerpt || t('notes.blank')}
                        </p>
                        <div className="mt-1 flex items-center gap-1.5 text-[11px] text-[var(--text-faint)]">
                          <span>{formatRelativeTime(note.modifiedAt)}</span>
                          {note.projectId !== 'root' && note.projectName && (
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
                          aria-label={t('notes.actions', { title: note.title })}
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
                            onSelect={() => void onPin(note, !note.pinned)}
                            className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
                          >
                            {note.pinned ? <PinOff size={13} /> : <Pin size={13} />}
                            {note.pinned ? t('notes.unpin') : t('notes.pin')}
                          </DropdownMenu.Item>
                          <DropdownMenu.Item
                            onSelect={() => void onDuplicate(note)}
                            className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
                          >
                            <Copy size={13} /> {t('notes.duplicate')}
                          </DropdownMenu.Item>
                          <DropdownMenu.Item
                            onSelect={() => setRenaming(note)}
                            className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
                          >
                            <Pencil size={13} /> {t('common.rename')}
                          </DropdownMenu.Item>
                          <DropdownMenu.Item
                            onSelect={() => setDeleting(note)}
                            className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-[var(--danger)] outline-none data-[highlighted]:bg-[var(--danger-soft)]"
                          >
                            <Trash2 size={13} /> {t('common.delete')}
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
        title={t('notes.renameTitle')}
        label={t('notes.titleLabel')}
        initialValue={renaming?.title ?? ''}
        confirmText={t('common.save')}
        onOpenChange={(open) => !open && setRenaming(null)}
        onSubmit={async (value) => {
          if (renaming) {
            await onRename(renaming, value);
          }
        }}
      />

      <ConfirmDialog
        open={deleting !== null}
        title={t('notes.deleteTitle', { title: deleting?.title ?? '' })}
        description={t('notes.deleteDescription')}
        confirmText={t('notes.moveToTrash')}
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
