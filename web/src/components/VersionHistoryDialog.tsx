import * as Dialog from '@radix-ui/react-dialog';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { diffLines, type Change } from 'diff';
import { History, Loader2, RotateCcw, X } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import type { Note, NoteVersion } from '../api/types';
import { useI18n } from '../lib/i18n';
import { useToastStore } from '../store/toast';

interface Props {
  noteId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  currentContent: string;
  currentEtag: string | null;
  onRestored: (note: Note) => void;
}

function formatDate(value: string, locale: string): string {
  return new Date(value).toLocaleString(locale, {
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

function formatSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  return `${(bytes / 1024).toFixed(1)} KB`;
}

export function VersionHistoryDialog({
  noteId,
  open,
  onOpenChange,
  currentContent,
  currentEtag,
  onRestored,
}: Props) {
  const { language, t, tn } = useI18n();
  const locale = language === 'zh' ? 'zh-CN' : 'en-US';
  const queryClient = useQueryClient();
  const toast = useToastStore((state) => state.push);
  const [selected, setSelected] = useState<string | null>(null);
  const [restoring, setRestoring] = useState(false);

  const versionsQuery = useQuery({
    queryKey: ['versions', noteId],
    queryFn: () => api.versions(noteId),
    enabled: open,
  });

  const versions = versionsQuery.data;
  const activeId = selected ?? versions?.[0]?.id ?? null;

  const detailQuery = useQuery({
    queryKey: ['version', noteId, activeId],
    queryFn: () => api.version(noteId, activeId!),
    enabled: open && activeId !== null,
  });

  useEffect(() => {
    if (!open) {
      setSelected(null);
    }
  }, [open]);

  const diff = useMemo<Change[]>(() => {
    if (!detailQuery.data) {
      return [];
    }
    return diffLines(detailQuery.data.content, currentContent);
  }, [detailQuery.data, currentContent]);

  const activeVersion = versions?.find((version) => version.id === activeId) ?? null;

  async function restore(version: NoteVersion) {
    setRestoring(true);
    try {
      const restored = await api.restoreVersion(noteId, version.id, currentEtag);
      queryClient.setQueryData<Note>(['note', noteId], restored);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['versions', noteId] }),
        queryClient.invalidateQueries({ queryKey: ['notes'] }),
      ]);
      onRestored(restored);
      toast(t('versions.restored'), 'success');
      onOpenChange(false);
    } catch (reason) {
      toast(reason instanceof Error ? reason.message : t('versions.restoreFailed'), 'error');
    } finally {
      setRestoring(false);
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/30 backdrop-blur-[1px]" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-50 flex h-[min(86vh,44rem)] w-[min(96vw,64rem)] -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-raised)] shadow-2xl outline-none">
          <header className="flex h-[52px] shrink-0 items-center gap-2 border-b border-[var(--border)] px-4">
            <History size={16} className="text-[var(--text-muted)]" />
            <Dialog.Title className="text-sm font-semibold">{t('versions.title')}</Dialog.Title>
            <span className="text-xs text-[var(--text-faint)]">
              {versions ? tn('versions.count', versions.length) : ''}
            </span>
            <div className="flex-1" />
            <Dialog.Close asChild>
              <button
                type="button"
                aria-label={t('common.close')}
                className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
              >
                <X size={16} />
              </button>
            </Dialog.Close>
          </header>

          <div className="flex min-h-0 flex-1">
            <aside className="w-[230px] shrink-0 overflow-y-auto border-r border-[var(--border)] bg-[var(--surface)] p-2">
              {versionsQuery.isLoading ? (
                <div className="grid place-items-center py-10">
                  <Loader2 size={16} className="animate-spin text-[var(--text-faint)]" />
                </div>
              ) : (versions?.length ?? 0) === 0 ? (
                <p className="px-3 py-8 text-center text-xs leading-relaxed text-[var(--text-faint)]">
                  {t('versions.empty')}
                </p>
              ) : (
                versions!.map((version) => {
                  const active = version.id === activeId;
                  return (
                    <button
                      key={version.id}
                      type="button"
                      onClick={() => setSelected(version.id)}
                      className={`mb-1 w-full rounded-lg px-2.5 py-2 text-left text-xs transition-colors ${
                        active
                          ? 'bg-[var(--accent-soft)] text-[var(--text)]'
                          : 'text-[var(--text-muted)] hover:bg-[var(--surface-hover)]'
                      }`}
                    >
                      <span className="flex items-center gap-1.5">
                        <span className="font-medium">{formatDate(version.createdAt, locale)}</span>
                        {version.isCurrent && (
                          <span className="rounded bg-[var(--accent)] px-1 py-0.5 text-[9px] font-medium text-white">
                            {t('versions.current')}
                          </span>
                        )}
                      </span>
                      <span className="mt-0.5 block text-[11px] text-[var(--text-faint)]">
                        {formatSize(version.size)}
                      </span>
                    </button>
                  );
                })
              )}
            </aside>

            <div className="flex min-w-0 flex-1 flex-col">
              <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
                {!activeId ? (
                  <div className="grid flex-1 place-items-center px-6 text-center text-sm text-[var(--text-faint)]">
                    {t('versions.pick')}
                  </div>
                ) : detailQuery.isLoading ? (
                  <div className="grid flex-1 place-items-center">
                    <Loader2 size={18} className="animate-spin text-[var(--text-faint)]" />
                  </div>
                ) : detailQuery.error ? (
                  <div className="grid flex-1 place-items-center px-6 text-center text-sm text-[var(--danger)]">
                    {detailQuery.error instanceof Error ? detailQuery.error.message : t('versions.readFailed')}
                  </div>
                ) : (
                  <>
                    <div className="flex shrink-0 items-center gap-3 border-b border-[var(--border)] px-4 py-2 text-[11px] text-[var(--text-faint)]">
                      <span className="text-[var(--danger)]">{t('versions.old')}</span>
                      <span className="text-[var(--accent-strong)]">{t('versions.new')}</span>
                      <span className="flex-1" />
                      <span>{activeVersion ? formatDate(activeVersion.createdAt, locale) : ''}</span>
                    </div>
                    <div className="min-h-0 flex-1 overflow-auto bg-[var(--surface)] font-mono text-[12.5px] leading-[1.7]">
                      {diff.map((part, index) => (
                        <div
                          key={index}
                          className={
                            part.added
                              ? 'bg-[var(--accent-soft)]'
                              : part.removed
                                ? 'bg-[var(--danger-soft)]'
                                : ''
                          }
                        >
                          {part.value.split('\n').map((line, lineIndex, lines) => {
                            if (lineIndex === lines.length - 1 && line === '') {
                              return null;
                            }
                            return (
                              <div
                                key={lineIndex}
                                className={`flex whitespace-pre-wrap break-words px-3 ${
                                  part.added
                                    ? 'text-[var(--accent-strong)]'
                                    : part.removed
                                      ? 'text-[var(--danger)]'
                                      : 'text-[var(--text-muted)]'
                                }`}
                              >
                                <span className="mr-2 w-3 shrink-0 select-none opacity-60">
                                  {part.added ? '+' : part.removed ? '−' : ' '}
                                </span>
                                <span>{line || ' '}</span>
                              </div>
                            );
                          })}
                        </div>
                      ))}
                    </div>
                  </>
                )}
              </div>

              <footer className="flex shrink-0 items-center justify-between gap-3 border-t border-[var(--border)] px-4 py-3">
                <p className="text-[11px] leading-relaxed text-[var(--text-faint)]">
                  {t('versions.restoreHint')}
                </p>
                <button
                  type="button"
                  disabled={!activeVersion || activeVersion.isCurrent || restoring}
                  onClick={() => activeVersion && void restore(activeVersion)}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--accent)] px-3.5 py-2 text-xs font-medium text-white disabled:cursor-not-allowed disabled:opacity-40"
                >
                  {restoring ? <Loader2 size={13} className="animate-spin" /> : <RotateCcw size={13} />}
                  {t('versions.restore')}
                </button>
              </footer>
            </div>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
