import * as Dialog from '@radix-ui/react-dialog';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Loader2, RotateCcw, Trash2, X } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api/client';
import type { TrashEntry, TrashRestoreResult } from '../api/types';
import { useI18n } from '../lib/i18n';
import { formatBytes, formatRelativeTime } from '../lib/utils';
import { useToastStore } from '../store/toast';
import { ConfirmDialog } from './Dialogs';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onRestored: (result: TrashRestoreResult) => void;
}

export function TrashDialog({ open, onOpenChange, onRestored }: Props) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const toast = useToastStore((state) => state.push);
  const [purging, setPurging] = useState<TrashEntry | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const { data: entries, isLoading } = useQuery({
    queryKey: ['trash'],
    queryFn: api.trash,
    enabled: open,
  });

  async function restore(entry: TrashEntry) {
    setBusyId(entry.id);
    try {
      const result = await api.restoreTrash(entry.id);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['trash'] }),
        queryClient.invalidateQueries({ queryKey: ['notes'] }),
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
      ]);
      onRestored(result);
      toast(entry.kind === 'project' ? t('trash.projectRestored') : t('trash.noteRestored'), 'success');
    } catch (reason) {
      toast(reason instanceof Error ? reason.message : t('trash.restoreFailed'), 'error');
    } finally {
      setBusyId(null);
    }
  }

  async function purge(entry: TrashEntry) {
    await api.purgeTrash(entry.id);
    await queryClient.invalidateQueries({ queryKey: ['trash'] });
  }

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/25 backdrop-blur-[1px]" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-50 flex max-h-[min(80vh,42rem)] w-[min(94vw,40rem)] -translate-x-1/2 -translate-y-1/2 flex-col rounded-2xl border border-[var(--border)] bg-[var(--surface-raised)] shadow-2xl">
          <div className="flex items-start justify-between gap-4 border-b border-[var(--border)] px-6 py-4">
            <div>
              <Dialog.Title className="flex items-center gap-2 text-base font-semibold">
                <Trash2 size={16} /> {t('trash.title')}
              </Dialog.Title>
              <Dialog.Description className="mt-0.5 text-sm text-[var(--text-muted)]">
                {t('trash.description')}
              </Dialog.Description>
            </div>
            <Dialog.Close asChild>
              <button
                type="button"
                className="rounded-md p-1 text-[var(--text-faint)] hover:bg-[var(--accent-soft)] hover:text-[var(--text)]"
                aria-label={t('common.close')}
              >
                <X size={16} />
              </button>
            </Dialog.Close>
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto px-3 py-2">
            {isLoading ? (
              <div className="grid place-items-center py-16">
                <Loader2 className="animate-spin text-[var(--text-faint)]" size={18} />
              </div>
            ) : (entries?.length ?? 0) === 0 ? (
              <div className="grid place-items-center py-16 text-sm text-[var(--text-faint)]">
                {t('trash.empty')}
              </div>
            ) : (
              <ul className="space-y-1">
                {(entries ?? []).map((entry) => (
                  <li
                    key={entry.id}
                    className="flex items-center gap-3 rounded-xl px-3 py-2.5 hover:bg-[var(--surface-hover)]"
                  >
                    <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-[var(--surface-sunken)] text-[var(--text-faint)]">
                      {entry.kind === 'project' ? <span className="text-sm">⌸</span> : <span className="text-sm">¶</span>}
                    </span>
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-[13px] font-medium">{entry.title}</div>
                      <div className="truncate text-[11px] text-[var(--text-faint)]">
                        {entry.originalPath} · {formatRelativeTime(entry.deletedAt)}
                        {entry.size > 0 ? ` · ${formatBytes(entry.size)}` : ''}
                      </div>
                    </div>
                    <button
                      type="button"
                      disabled={busyId === entry.id}
                      onClick={() => void restore(entry)}
                      className="inline-flex items-center gap-1 rounded-lg border border-[var(--border)] px-2.5 py-1.5 text-xs hover:bg-[var(--accent-soft)] disabled:opacity-50"
                    >
                      {busyId === entry.id ? (
                        <Loader2 size={12} className="animate-spin" />
                      ) : (
                        <RotateCcw size={12} />
                      )}
                      {t('trash.restore')}
                    </button>
                    <button
                      type="button"
                      onClick={() => setPurging(entry)}
                      className="rounded-lg p-1.5 text-[var(--text-faint)] hover:bg-[var(--danger-soft)] hover:text-[var(--danger)]"
                      title={t('trash.purge')}
                    >
                      <Trash2 size={14} />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>

      <ConfirmDialog
        open={purging !== null}
        title={t('trash.purgeTitle', { title: purging?.title ?? '' })}
        description={t('trash.purgeDescription')}
        confirmText={t('trash.purgeConfirm')}
        onOpenChange={(next) => !next && setPurging(null)}
        onConfirm={async () => {
          if (purging) {
            await purge(purging);
          }
        }}
      />
    </Dialog.Root>
  );
}
