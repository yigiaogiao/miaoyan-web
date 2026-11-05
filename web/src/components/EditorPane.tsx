import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  AlertTriangle,
  Check,
  Columns2,
  Eye,
  List,
  Loader2,
  Monitor,
  Moon,
  MoreHorizontal,
  PanelLeft,
  Pencil,
  PenLine,
  RotateCcw,
  Sun,
  Trash2,
  UploadCloud,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { api, isConflictError } from '../api/client';
import type { ConflictInfo, Note, ThemeMode, ViewMode } from '../api/types';
import { formatRelativeTime, isMod } from '../lib/utils';
import { useToastStore } from '../store/toast';
import { useUiStore } from '../store/ui';
import { CodeEditor, type CodeEditorHandle } from './CodeEditor';
import { MarkdownPreview } from './MarkdownPreview';
import { ConfirmDialog } from './Dialogs';

interface Props {
  noteId: string;
  viewMode: ViewMode;
  onViewMode: (mode: ViewMode) => void;
  onRename: (note: Note) => void;
  onDelete: (note: Note) => void;
}

type SaveState = 'saved' | 'dirty' | 'saving' | 'error' | 'conflict';

const THEME_ITEMS: { value: ThemeMode; label: string; icon: ReactNode }[] = [
  { value: 'system', label: '跟随系统', icon: <Monitor size={14} /> },
  { value: 'light', label: '浅色', icon: <Sun size={14} /> },
  { value: 'dark', label: '深色', icon: <Moon size={14} /> },
];

function countWords(content: string): number {
  const withoutFrontmatter = content.replace(/^---\n[\s\S]*?\n---\n?/, '');
  const cjk = (withoutFrontmatter.match(/[㐀-鿿豈-﫿]/g) ?? []).length;
  const latin = (withoutFrontmatter.match(/[A-Za-z0-9_]+(?:['’-][A-Za-z0-9_]+)*/g) ?? []).length;
  return cjk + latin;
}

export function EditorPane({ noteId, viewMode, onViewMode, onRename, onDelete }: Props) {
  const queryClient = useQueryClient();
  const toast = useToastStore((state) => state.push);
  const theme = useUiStore((state) => state.theme);
  const setTheme = useUiStore((state) => state.setTheme);
  const toggleSidebar = useUiStore((state) => state.toggleSidebar);
  const toggleList = useUiStore((state) => state.toggleList);
  const sidebarOpen = useUiStore((state) => state.sidebarOpen);
  const listOpen = useUiStore((state) => state.listOpen);

  const { data: note, error, isLoading } = useQuery({
    queryKey: ['note', noteId],
    queryFn: () => api.note(noteId),
  });

  const [content, setContent] = useState('');
  const [saveState, setSaveState] = useState<SaveState>('saved');
  const [externalUpdate, setExternalUpdate] = useState(false);
  const [conflict, setConflict] = useState<ConflictInfo | null>(null);
  const [uploading, setUploading] = useState(0);
  const [deleting, setDeleting] = useState(false);

  const editorRef = useRef<CodeEditorHandle>(null);
  const etagRef = useRef<string | null>(null);
  const savedContentRef = useRef('');
  const contentRef = useRef('');
  const dirtyRef = useRef(false);
  const savingRef = useRef(false);
  const pendingRef = useRef(false);
  const loadedIdRef = useRef<string | null>(null);
  const deletingRef = useRef(false);
  const noteRef = useRef<Note | null>(null);
  noteRef.current = note ?? null;

  const handleChange = useCallback((value: string) => {
    contentRef.current = value;
    setContent(value);
  }, []);

  const adopt = useCallback((value: Note) => {
    contentRef.current = value.content;
    savedContentRef.current = value.content;
    etagRef.current = value.etag;
    dirtyRef.current = false;
    loadedIdRef.current = value.id;
    setContent(value.content);
    setSaveState('saved');
    setExternalUpdate(false);
    setConflict(null);
  }, []);

  useEffect(() => {
    if (!note) {
      return;
    }

    const isNewNote = note.id !== loadedIdRef.current;
    const etagChanged = note.etag !== etagRef.current;

    if (isNewNote) {
      adopt(note);
      return;
    }

    if (etagChanged && dirtyRef.current) {
      setExternalUpdate(true);
      return;
    }

    if (!dirtyRef.current && (etagChanged || note.content !== contentRef.current)) {
      adopt(note);
    }
  }, [note, adopt]);

  const saveNow = useCallback(
    async (options?: { force?: boolean }) => {
      const noteValue = noteRef.current;
      if (!noteValue) {
        return;
      }

      if (savingRef.current) {
        pendingRef.current = true;
        return;
      }

      const payload = contentRef.current;
      if (!options?.force && payload === savedContentRef.current) {
        setSaveState('saved');
        return;
      }

      savingRef.current = true;
      setSaveState('saving');
      try {
        const result = await api.saveNote(noteValue.id, {
          content: payload,
          etag: etagRef.current,
        });

        etagRef.current = result.etag;
        savedContentRef.current = payload;
        const stillDirty = contentRef.current !== payload;
        dirtyRef.current = stillDirty;
        setSaveState(stillDirty ? 'dirty' : 'saved');
        setExternalUpdate(false);
        setConflict(null);

        queryClient.setQueryData<Note>(['note', noteValue.id], (current) =>
          current ? { ...current, ...result } : result,
        );
        void queryClient.invalidateQueries({ queryKey: ['notes'], exact: false });

        if (pendingRef.current) {
          pendingRef.current = false;
          if (contentRef.current !== savedContentRef.current) {
            void saveNow();
          }
        }
      } catch (reason) {
        if (isConflictError(reason)) {
          setConflict(reason.body);
          setSaveState('conflict');
        } else {
          setSaveState('error');
          toast(reason instanceof Error ? reason.message : '保存失败', 'error');
        }
      } finally {
        savingRef.current = false;
      }
    },
    [queryClient, toast],
  );

  useEffect(() => {
    if (content === savedContentRef.current) {
      dirtyRef.current = false;
      return;
    }

    dirtyRef.current = true;
    setSaveState('dirty');

    if (externalUpdate) {
      return;
    }

    const timer = window.setTimeout(() => {
      void saveNow();
    }, 650);
    return () => window.clearTimeout(timer);
  }, [content, externalUpdate, saveNow]);

  useEffect(() => {
    document.title = note ? `${note.title} · 妙言 Web` : '妙言 Web';
  }, [note?.title]);

  useEffect(
    () => () => {
      const current = noteRef.current;
      if (!deletingRef.current && current && dirtyRef.current) {
        void api
          .saveNote(current.id, { content: contentRef.current, etag: etagRef.current })
          .catch(() => undefined);
      }
    },
    [],
  );

  useEffect(() => {
    function flush() {
      const current = noteRef.current;
      if (!current || !dirtyRef.current || deletingRef.current) {
        return;
      }
      void fetch(`/api/notes/${encodeURIComponent(current.id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: contentRef.current, etag: etagRef.current }),
        keepalive: true,
      }).catch(() => undefined);
    }

    window.addEventListener('pagehide', flush);
    return () => window.removeEventListener('pagehide', flush);
  }, []);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (isMod(event) && event.key.toLowerCase() === 's') {
        event.preventDefault();
        void saveNow({ force: true });
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [saveNow]);

  const uploadFiles = useCallback(
    async (files: File[], position: number | null) => {
      if (!noteRef.current) {
        return;
      }

      setUploading((count) => count + files.length);
      let insertAt = position;
      try {
        for (const file of files) {
          const asset = await api.uploadAsset(noteRef.current.id, file);
          const text = `${asset.markdown}\n`;
          editorRef.current?.insertText(text, insertAt ?? undefined);
          if (insertAt !== null) {
            insertAt += text.length;
          }
        }
      } catch (reason) {
        toast(reason instanceof Error ? reason.message : '附件上传失败', 'error');
      } finally {
        setUploading((count) => Math.max(0, count - files.length));
      }
    },
    [toast],
  );

  const restoreFromDisk = useCallback(() => {
    const current = noteRef.current;
    if (!current) {
      return;
    }
    adopt({ ...current, content: conflict?.currentContent ?? current.content });
    if (conflict) {
      etagRef.current = conflict.currentEtag;
    }
    void queryClient.invalidateQueries({ queryKey: ['note', current.id] });
    setConflict(null);
  }, [adopt, conflict, queryClient]);

  const keepMine = useCallback(async () => {
    if (conflict) {
      etagRef.current = conflict.currentEtag;
      setConflict(null);
      setExternalUpdate(false);
      await saveNow({ force: true });
    }
  }, [conflict, saveNow]);

  const words = useMemo(() => countWords(content), [content]);

  if (isLoading) {
    return (
      <div className="grid h-full place-items-center">
        <Loader2 className="animate-spin text-[var(--text-faint)]" size={20} />
      </div>
    );
  }

  if (error || !note) {
    return (
      <div className="grid h-full place-items-center px-8 text-center">
        <div>
          <AlertTriangle className="mx-auto mb-3 text-[var(--danger)]" size={22} />
          <p className="text-sm text-[var(--text-muted)]">
            {error instanceof Error ? error.message : '笔记不存在或已被移动'}
          </p>
        </div>
      </div>
    );
  }

  const statusLabel = {
    saved: '已保存',
    dirty: '未保存',
    saving: '正在保存…',
    error: '保存失败',
    conflict: '有冲突',
  }[saveState];

  return (
    <section className="flex h-full min-w-0 flex-1 flex-col bg-[var(--surface)]">
      <header className="flex h-[52px] shrink-0 items-center gap-1.5 border-b border-[var(--border)] px-3">
        {!sidebarOpen && (
          <button
            type="button"
            title="显示项目栏 (⌘1)"
            onClick={toggleSidebar}
            className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
          >
            <PanelLeft size={16} />
          </button>
        )}
        {!listOpen && (
          <button
            type="button"
            title="显示笔记列表 (⌘2)"
            onClick={toggleList}
            className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
          >
            <List size={16} />
          </button>
        )}

        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5 text-[13px] text-[var(--text-faint)]">
            {note.projectName && <span className="truncate">{note.projectName}</span>}
            {note.projectName && <span>/</span>}
            <span className="truncate font-medium text-[var(--text)]">{note.title}</span>
          </div>
          <div className="text-[11px] text-[var(--text-faint)]">
            {formatRelativeTime(note.modifiedAt)} · {words} 字
            {uploading > 0 && ` · 正在上传 ${uploading} 个附件`}
          </div>
        </div>

        <span
          className={`hidden items-center gap-1 rounded-full px-2 py-1 text-[11px] sm:flex ${
            saveState === 'error' || saveState === 'conflict'
              ? 'bg-[var(--danger-soft)] text-[var(--danger)]'
              : 'text-[var(--text-faint)]'
          }`}
        >
          {saveState === 'saving' ? (
            <Loader2 size={11} className="animate-spin" />
          ) : saveState === 'error' || saveState === 'conflict' ? (
            <AlertTriangle size={11} />
          ) : saveState === 'dirty' ? (
            <span className="block size-1.5 rounded-full bg-[var(--warning)]" />
          ) : (
            <Check size={11} />
          )}
          {statusLabel}
        </span>

        <div className="flex items-center rounded-lg bg-[var(--surface-sunken)] p-0.5">
          <button
            type="button"
            title="仅编辑"
            onClick={() => onViewMode('editor')}
            className={`rounded-md p-1.5 ${
              viewMode === 'editor'
                ? 'bg-[var(--surface-raised)] text-[var(--accent-strong)] shadow-sm'
                : 'text-[var(--text-faint)] hover:text-[var(--text)]'
            }`}
          >
            <PenLine size={15} />
          </button>
          <button
            type="button"
            title="分栏"
            onClick={() => onViewMode('split')}
            className={`hidden rounded-md p-1.5 sm:block ${
              viewMode === 'split'
                ? 'bg-[var(--surface-raised)] text-[var(--accent-strong)] shadow-sm'
                : 'text-[var(--text-faint)] hover:text-[var(--text)]'
            }`}
          >
            <Columns2 size={15} />
          </button>
          <button
            type="button"
            title="仅预览"
            onClick={() => onViewMode('preview')}
            className={`rounded-md p-1.5 ${
              viewMode === 'preview'
                ? 'bg-[var(--surface-raised)] text-[var(--accent-strong)] shadow-sm'
                : 'text-[var(--text-faint)] hover:text-[var(--text)]'
            }`}
          >
            <Eye size={15} />
          </button>
        </div>

        <DropdownMenu.Root>
          <DropdownMenu.Trigger asChild>
            <button
              type="button"
              title={`主题：${THEME_ITEMS.find((item) => item.value === theme)?.label}`}
              className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
            >
              {theme === 'dark' ? <Moon size={15} /> : theme === 'light' ? <Sun size={15} /> : <Monitor size={15} />}
            </button>
          </DropdownMenu.Trigger>
          <DropdownMenu.Portal>
            <DropdownMenu.Content
              align="end"
              sideOffset={4}
              className="z-50 min-w-36 rounded-lg border border-[var(--border)] bg-[var(--surface-raised)] p-1 text-[13px] shadow-xl"
            >
              {THEME_ITEMS.map((item) => (
                <DropdownMenu.Item
                  key={item.value}
                  onSelect={() => setTheme(item.value)}
                  className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
                >
                  {item.icon}
                  <span className="flex-1">{item.label}</span>
                  {theme === item.value && <Check size={13} className="text-[var(--accent)]" />}
                </DropdownMenu.Item>
              ))}
            </DropdownMenu.Content>
          </DropdownMenu.Portal>
        </DropdownMenu.Root>

        <DropdownMenu.Root>
          <DropdownMenu.Trigger asChild>
            <button
              type="button"
              title="更多操作"
              className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
            >
              <MoreHorizontal size={16} />
            </button>
          </DropdownMenu.Trigger>
          <DropdownMenu.Portal>
            <DropdownMenu.Content
              align="end"
              sideOffset={4}
              className="z-50 min-w-40 rounded-lg border border-[var(--border)] bg-[var(--surface-raised)] p-1 text-[13px] shadow-xl"
            >
              <DropdownMenu.Item
                onSelect={() => onRename(note)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <Pencil size={13} /> 重命名
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => {
                  void navigator.clipboard
                    .writeText(contentRef.current)
                    .then(() => toast('已复制 Markdown', 'success'))
                    .catch(() => toast('复制失败', 'error'));
                }}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <UploadCloud size={13} /> 复制全文
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => setDeleting(true)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-[var(--danger)] outline-none data-[highlighted]:bg-[var(--danger-soft)]"
              >
                <Trash2 size={13} /> 删除笔记
              </DropdownMenu.Item>
            </DropdownMenu.Content>
          </DropdownMenu.Portal>
        </DropdownMenu.Root>
      </header>

      {externalUpdate && (
        <div className="flex items-center gap-2 border-b border-[var(--border)] bg-[var(--warning-soft)] px-3 py-2 text-xs text-[var(--warning)]">
          <AlertTriangle size={13} />
          <span className="flex-1">磁盘上的文件已在外部更新，你还有未保存的修改。</span>
          <button
            type="button"
            onClick={restoreFromDisk}
            className="rounded-md border border-current/30 px-2 py-1 hover:bg-white/20"
          >
            载入磁盘版本
          </button>
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {viewMode !== 'preview' && (
          <div style={{ flex: viewMode === 'split' ? '1 1 50%' : '1 1 100%' }} className="min-w-0">
            <CodeEditor
              ref={editorRef}
              value={content}
              onChange={handleChange}
              onSave={() => void saveNow({ force: true })}
              onAttachFiles={(files, position) => void uploadFiles(files, position)}
            />
          </div>
        )}

        {viewMode === 'split' && (
          <div className="w-px shrink-0 bg-[var(--border)]" aria-hidden />
        )}

        {viewMode !== 'editor' && (
          <div
            style={{ flex: viewMode === 'split' ? '1 1 50%' : '1 1 100%' }}
            className="min-w-0 bg-[var(--bg)]"
          >
            <MarkdownPreview content={content} noteId={note.id} />
          </div>
        )}
      </div>

      <ConfirmDialog
        open={deleting}
        title={`删除「${note.title}」？`}
        description="笔记会移入回收站，可以随时恢复。"
        confirmText="移入回收站"
        onOpenChange={setDeleting}
        onConfirm={() => {
          deletingRef.current = true;
          return onDelete(note);
        }}
      />

      <ConflictDialog
        conflict={conflict}
        onLoadDisk={restoreFromDisk}
        onKeepMine={() => void keepMine()}
        onClose={() => setConflict(null)}
      />
    </section>
  );
}

function ConflictDialog({
  conflict,
  onLoadDisk,
  onKeepMine,
  onClose,
}: {
  conflict: ConflictInfo | null;
  onLoadDisk: () => void;
  onKeepMine: () => void;
  onClose: () => void;
}) {
  return (
    <div
      className={`fixed inset-0 z-40 grid place-items-center bg-black/30 px-4 ${
        conflict ? '' : 'pointer-events-none opacity-0'
      }`}
      aria-hidden={!conflict}
    >
      {conflict && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="conflict-title"
          className="w-[min(94vw,30rem)] rounded-2xl border border-[var(--border)] bg-[var(--surface-raised)] p-6 shadow-2xl"
        >
          <div className="flex items-start gap-3">
            <span className="mt-0.5 grid size-9 shrink-0 place-items-center rounded-full bg-[var(--danger-soft)] text-[var(--danger)]">
              <RotateCcw size={16} />
            </span>
            <div>
              <h3 id="conflict-title" className="text-base font-semibold">文件已在磁盘上被修改</h3>
              <p className="mt-1 text-sm leading-relaxed text-[var(--text-muted)]">
                保存时发现文件被其他程序改动。你的这份内容已自动备份到：
                <code className="mt-1 block break-all rounded bg-[var(--surface-sunken)] px-2 py-1 text-[11px]">
                  {conflict.conflictPath}
                </code>
              </p>
            </div>
          </div>
          <div className="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg px-3.5 py-2 text-sm text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
            >
              稍后处理
            </button>
            <button
              type="button"
              onClick={onLoadDisk}
              className="rounded-lg border border-[var(--border-strong)] px-3.5 py-2 text-sm hover:bg-[var(--accent-soft)]"
            >
              载入磁盘版本
            </button>
            <button
              type="button"
              onClick={onKeepMine}
              className="rounded-lg bg-[var(--accent)] px-3.5 py-2 text-sm font-medium text-white"
            >
              保留我的文本
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
