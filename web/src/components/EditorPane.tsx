import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  AlertTriangle,
  AudioLines,
  Bold,
  Check,
  Code2,
  Columns2,
  Copy,
  Eye,
  FileCode2,
  FileText,
  Focus,
  Heading1,
  Heading2,
  Heading3,
  History,
  Italic,
  Link,
  Link2,
  List,
  ListOrdered,
  ListTodo,
  ListTree,
  Loader2,
  Minus,
  Monitor,
  Moon,
  MoreHorizontal,
  PanelLeft,
  Pencil,
  PenLine,
  Pin,
  PinOff,
  Presentation,
  Printer,
  Quote,
  RotateCcw,
  ScanText,
  Settings2,
  Sigma,
  SpellCheck,
  Strikethrough,
  Sun,
  Table2,
  Trash2,
  Wand2,
  Workflow,
  X,
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router';
import { api, isConflictError } from '../api/client';
import type { ConflictInfo, Note, ThemeMode, ViewMode } from '../api/types';
import { exportHtml, exportMarkdown, exportPdf } from '../lib/export';
import { tidyMarkdown } from '../lib/format';
import { useI18n } from '../lib/i18n';
import { extractHeadings } from '../lib/markdown';
import { cleanTypography } from '../lib/typography';
import { formatRelativeTime, isMod } from '../lib/utils';
import { useToastStore } from '../store/toast';
import { useUiStore } from '../store/ui';
import { CodeEditor, type CodeEditorHandle } from './CodeEditor';
import { MarkdownPreview, type MarkdownPreviewHandle } from './MarkdownPreview';
import { ConfirmDialog } from './Dialogs';
import { PptView } from './PptView';
import { VersionHistoryDialog } from './VersionHistoryDialog';

interface Props {
  noteId: string;
  viewMode: ViewMode;
  onViewMode: (mode: ViewMode) => void;
  onRename: (note: Note) => void;
  onDelete: (note: Note) => void;
  onPin: (note: Note, pinned: boolean) => void;
  onDuplicate: (note: Note) => void;
}

type SaveState = 'saved' | 'dirty' | 'saving' | 'error' | 'conflict';

const THEME_ITEMS: { value: ThemeMode; key: string; icon: ReactNode }[] = [
  { value: 'system', key: 'theme.system', icon: <Monitor size={14} /> },
  { value: 'light', key: 'theme.light', icon: <Sun size={14} /> },
  { value: 'dark', key: 'theme.dark', icon: <Moon size={14} /> },
];

const FRONTMATTER_BLOCK = /^---\r?\n[\s\S]*?\r?\n---(?:\r?\n|$)/;

function countWords(content: string): number {
  const withoutFrontmatter = content.replace(/^---\n[\s\S]*?\n---\n?/, '');
  const cjk = (withoutFrontmatter.match(/[㐀-鿿豈-﫿]/g) ?? []).length;
  const latin = (withoutFrontmatter.match(/[A-Za-z0-9_]+(?:['’-][A-Za-z0-9_]+)*/g) ?? []).length;
  return cjk + latin;
}

export function EditorPane({
  noteId,
  viewMode,
  onViewMode,
  onRename,
  onDelete,
  onPin,
  onDuplicate,
}: Props) {
  const { language, t, tn } = useI18n();
  const setLanguage = useUiStore((state) => state.setLanguage);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const toast = useToastStore((state) => state.push);
  const theme = useUiStore((state) => state.theme);
  const setTheme = useUiStore((state) => state.setTheme);
  const toggleSidebar = useUiStore((state) => state.toggleSidebar);
  const toggleList = useUiStore((state) => state.toggleList);
  const sidebarOpen = useUiStore((state) => state.sidebarOpen);
  const listOpen = useUiStore((state) => state.listOpen);
  const focusMode = useUiStore((state) => state.focusMode);
  const setFocusMode = useUiStore((state) => state.setFocusMode);
  const typewriter = useUiStore((state) => state.typewriter);
  const setTypewriter = useUiStore((state) => state.setTypewriter);
  const editorFontSize = useUiStore((state) => state.editorFontSize);
  const setEditorFontSize = useUiStore((state) => state.setEditorFontSize);
  const editorLineHeight = useUiStore((state) => state.editorLineHeight);
  const setEditorLineHeight = useUiStore((state) => state.setEditorLineHeight);
  const editorWidth = useUiStore((state) => state.editorWidth);
  const setEditorWidth = useUiStore((state) => state.setEditorWidth);

  const { data: note, error, isLoading, refetch } = useQuery({
    queryKey: ['note', noteId],
    queryFn: () => api.note(noteId),
    retry: false,
  });

  const linksQuery = useQuery({
    queryKey: ['links'],
    queryFn: api.links,
    staleTime: 5_000,
  });

  const backlinksQuery = useQuery({
    queryKey: ['backlinks', noteId],
    queryFn: () => api.backlinks(noteId),
    enabled: Boolean(note),
    retry: false,
  });

  const linkTargets = useMemo(
    () =>
      new Map(
        (linksQuery.data?.targets ?? []).map((target) => [target.title.toLowerCase(), target]),
      ),
    [linksQuery.data],
  );
  const linkTitles = useMemo(
    () => new Set((linksQuery.data?.targets ?? []).map((target) => target.title)),
    [linksQuery.data],
  );

  const [showBacklinks, setShowBacklinks] = useState(false);
  const [outlineOpen, setOutlineOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [versionsOpen, setVersionsOpen] = useState(false);
  const [pptOpen, setPptOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [content, setContent] = useState('');
  const [saveState, setSaveState] = useState<SaveState>('saved');
  const [externalUpdate, setExternalUpdate] = useState(false);
  const [conflict, setConflict] = useState<ConflictInfo | null>(null);
  const [uploading, setUploading] = useState(0);
  const [deleting, setDeleting] = useState(false);

  const editorRef = useRef<CodeEditorHandle>(null);
  const previewRef = useRef<MarkdownPreviewHandle>(null);
  const ocrInputRef = useRef<HTMLInputElement>(null);
  const transcribeInputRef = useRef<HTMLInputElement>(null);
  const syncSource = useRef<'editor' | 'preview' | null>(null);
  const syncTimer = useRef(0);
  const etagRef = useRef<string | null>(null);
  const savedContentRef = useRef('');
  const contentRef = useRef('');
  const dirtyRef = useRef(false);
  const savingRef = useRef(false);
  const pendingRef = useRef(false);
  const loadedIdRef = useRef<string | null>(null);
  const deletingRef = useRef(false);
  const noteRef = useRef<Note | null>(null);
  const frontmatterRef = useRef<string | null>(null);
  noteRef.current = note ?? null;

  const handleChange = useCallback((value: string) => {
    // The editor hides the managed frontmatter, so select-all + delete would
    // silently drop id/created/…. Keep the block on save; it is not prose.
    const frontmatter = frontmatterRef.current;
    const next =
      frontmatter && !value.startsWith('---') ? frontmatter + value : value;
    contentRef.current = next;
    setContent(next);
  }, []);

  const releaseSync = useCallback(() => {
    window.clearTimeout(syncTimer.current);
    syncTimer.current = window.setTimeout(() => {
      syncSource.current = null;
    }, 180);
  }, []);

  const syncFromEditor = useCallback(
    (line: number) => {
      if (viewMode !== 'split' || syncSource.current === 'preview') {
        return;
      }
      syncSource.current = 'editor';
      previewRef.current?.scrollToLine(line);
      releaseSync();
    },
    [releaseSync, viewMode],
  );

  const syncFromPreview = useCallback(
    (line: number) => {
      if (viewMode !== 'split' || syncSource.current === 'editor') {
        return;
      }
      syncSource.current = 'preview';
      editorRef.current?.scrollToLine(line);
      releaseSync();
    },
    [releaseSync, viewMode],
  );

  const adopt = useCallback((value: Note) => {
    frontmatterRef.current = value.content.match(FRONTMATTER_BLOCK)?.[0] ?? null;
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
        void queryClient.invalidateQueries({ queryKey: ['links'] });
        void queryClient.invalidateQueries({ queryKey: ['backlinks'] });

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
          toast(reason instanceof Error ? reason.message : t('editor.saveFailed'), 'error');
        }
      } finally {
        savingRef.current = false;
      }
    },
    [queryClient, toast, t],
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
    document.title = note ? `${note.title} · ${t('brand')}` : t('brand');
  }, [note?.title, t]);

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
      if (isMod(event) && event.altKey && event.code === 'KeyP') {
        event.preventDefault();
        setPptOpen(true);
        return;
      }
      if (isMod(event) && event.shiftKey && event.code === 'KeyF') {
        event.preventDefault();
        setFocusMode(!focusMode);
        return;
      }
      if (isMod(event) && event.key.toLowerCase() === 's') {
        event.preventDefault();
        void saveNow({ force: true });
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [focusMode, saveNow, setFocusMode]);

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
        toast(reason instanceof Error ? reason.message : t('editor.attachmentFailed'), 'error');
      } finally {
        setUploading((count) => Math.max(0, count - files.length));
      }
    },
    [toast, t],
  );

  const runMediaTool = useCallback(
    async (kind: 'ocr' | 'transcribe', files: FileList | null) => {
      const file = files?.[0];
      const current = noteRef.current;
      if (!file || !current) {
        return;
      }

      try {
        const asset = await api.uploadAsset(current.id, file);
        const path = `${asset.kind === 'image' ? 'i' : 'files'}/${asset.name}`;
        toast(kind === 'ocr' ? t('tools.ocrRunning') : t('tools.transcribeRunning'), 'info');
        const result =
          kind === 'ocr'
            ? await api.ocrAsset(current.id, path)
            : await api.transcribeAsset(current.id, path);
        if (!result.text) {
          toast(kind === 'ocr' ? t('tools.ocrEmpty') : t('tools.transcribeEmpty'), 'error');
          return;
        }

        editorRef.current?.insertText(`${asset.markdown}\n\n${result.text}\n`);
        toast(kind === 'ocr' ? t('tools.ocrDone') : t('tools.transcribeDone'), 'success');
      } catch (reason) {
        toast(reason instanceof Error ? reason.message : t('editor.processingFailed'), 'error');
      }
    },
    [toast, t],
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

  const openWikilink = useCallback(
    async (title: string, unresolved: boolean) => {
      if (!unresolved) {
        const target = linkTargets.get(title.toLowerCase());
        if (target) {
          navigate(`/note/${target.noteId}`);
          return;
        }
      }

      const current = noteRef.current;
      if (!current) {
        return;
      }

      try {
        const created = await api.createNote(current.projectId, title);
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: ['notes'] }),
          queryClient.invalidateQueries({ queryKey: ['projects'] }),
          queryClient.invalidateQueries({ queryKey: ['links'] }),
        ]);
        toast(t('editor.created', { title }), 'success');
        navigate(`/note/${created.id}`);
      } catch (reason) {
        toast(reason instanceof Error ? reason.message : t('editor.createFailed'), 'error');
      }
    },
    [linkTargets, navigate, queryClient, toast, t],
  );

  const handleVersionRestored = useCallback(
    (updated: Note) => {
      adopt(updated);
      void queryClient.invalidateQueries({ queryKey: ['notes'] });
      void queryClient.invalidateQueries({ queryKey: ['links'] });
      void queryClient.invalidateQueries({ queryKey: ['backlinks'] });
    },
    [adopt, queryClient],
  );

  const words = useMemo(() => countWords(content), [content]);
  const readingMinutes = Math.max(1, Math.ceil(words / 300));
  const headings = useMemo(() => extractHeadings(content), [content]);

  const handleTidy = useCallback(() => {
    const next = tidyMarkdown(contentRef.current);
    if (next === contentRef.current) {
      toast(t('editor.tidyAlready'), 'success');
      return;
    }
    editorRef.current?.replaceAll(next);
    toast(t('editor.tidyDone'), 'success');
  }, [toast, t]);

  const handleCleanTypography = useCallback(() => {
    const next = cleanTypography(contentRef.current);
    if (next === contentRef.current) {
      toast(t('editor.typographyAlready'), 'info');
      return;
    }
    editorRef.current?.replaceAll(next);
    toast(t('editor.typographyDone'), 'success');
  }, [toast, t]);

  const handleExportHtml = useCallback(async () => {
    const current = noteRef.current;
    if (!current) {
      return;
    }
    setExporting(true);
    try {
      await exportHtml({
        title: current.title,
        content: contentRef.current,
        noteId: current.id,
        linkTitles,
      });
      toast(t('editor.exportedHtml'), 'success');
    } catch (reason) {
      toast(reason instanceof Error ? reason.message : t('editor.exportFailed'), 'error');
    } finally {
      setExporting(false);
    }
  }, [linkTitles, toast, t]);

  const handleExportPdf = useCallback(async () => {
    const current = noteRef.current;
    if (!current) {
      return;
    }
    // Open during the click so popup blockers keep the preview window.
    const target = window.open('', '_blank');
    setExporting(true);
    try {
      await exportPdf({
        title: current.title,
        content: contentRef.current,
        noteId: current.id,
        linkTitles,
        target,
      });
    } catch (reason) {
      target?.close();
      toast(reason instanceof Error ? reason.message : t('editor.exportPdfFailed'), 'error');
    } finally {
      setExporting(false);
    }
  }, [linkTitles, toast, t]);

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
            {error instanceof Error ? error.message : t('editor.noteMissing')}
          </p>
          <p className="mt-1 text-xs text-[var(--text-faint)]">
            {t('editor.noteMissingHint')}
          </p>
          <div className="mt-4 flex items-center justify-center gap-2">
            <button
              type="button"
              onClick={() => void refetch()}
              className="rounded-lg border border-[var(--border-strong)] px-3 py-1.5 text-xs hover:bg-[var(--accent-soft)]"
            >
              {t('common.retry')}
            </button>
            <button
              type="button"
              onClick={() => navigate('/')}
              className="rounded-lg bg-[var(--accent)] px-3 py-1.5 text-xs font-medium text-white"
            >
              {t('editor.backToList')}
            </button>
          </div>
        </div>
      </div>
    );
  }

  const statusLabel = {
    saved: t('editor.saved'),
    dirty: t('editor.dirty'),
    saving: t('editor.saving'),
    error: t('editor.error'),
    conflict: t('editor.conflict'),
  }[saveState];

  return (
    <section className="relative flex h-full min-w-0 flex-1 flex-col bg-[var(--surface)]">
      <header className="flex h-[52px] shrink-0 items-center gap-1 border-b border-[var(--border)] px-4">
        {!sidebarOpen && (
          <button
            type="button"
            title={t('editor.showSidebar')}
            onClick={toggleSidebar}
            className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--surface-hover)]"
          >
            <PanelLeft size={16} />
          </button>
        )}
        {!listOpen && (
          <button
            type="button"
            title={t('editor.showList')}
            onClick={toggleList}
            className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--surface-hover)]"
          >
            <List size={16} />
          </button>
        )}

        {focusMode && (
          <button
            type="button"
            title={t('editor.exitFocus')}
            onClick={() => setFocusMode(false)}
            className="flex shrink-0 items-center gap-1 rounded-lg bg-[var(--accent-soft)] px-2 py-1 text-[11px] text-[var(--accent-strong)]"
          >
            <Focus size={13} />
            {t('editor.focus')}
          </button>
        )}

        <div className="min-w-0 flex-1">
          <div className="flex items-baseline gap-1.5 text-[12px] text-[var(--text-faint)]">
            {note.projectName && <span className="truncate">{note.projectName}</span>}
            {note.projectName && <span className="opacity-60">/</span>}
            <button
              type="button"
              onClick={() => onRename(note)}
              title={t('notes.renameTitle')}
              aria-label={t('notes.renameTitle')}
              className="group flex min-w-0 items-center gap-1 rounded-md text-left outline-none hover:bg-[var(--surface-hover)] focus-visible:ring-2 focus-visible:ring-[var(--accent)]"
            >
              <span className="truncate text-[13px] font-semibold tracking-tight text-[var(--text)]">
                {note.title}
              </span>
              <Pencil
                size={11}
                className="shrink-0 text-[var(--text-faint)] opacity-40 transition-opacity group-hover:opacity-100"
              />
            </button>
          </div>
          <div className="mt-0.5 text-[10.5px] text-[var(--text-faint)]">
            {formatRelativeTime(note.modifiedAt)} · {t('editor.words', { count: words })} ·{' '}
            {t('editor.readingTime', { minutes: readingMinutes })}
            {uploading > 0 && ` · ${tn('editor.uploading', uploading)}`}
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
            title={t('editor.modeEditor')}
            onClick={() => onViewMode('editor')}
            className={`rounded-md p-1.5 ${
              viewMode === 'editor'
                ? 'bg-[var(--surface-raised)] text-[var(--text)] shadow-sm'
                : 'text-[var(--text-faint)] hover:text-[var(--text)]'
            }`}
          >
            <PenLine size={15} />
          </button>
          <button
            type="button"
            title={t('editor.modeSplit')}
            onClick={() => onViewMode('split')}
            className={`hidden rounded-md p-1.5 min-[1500px]:block ${
              viewMode === 'split'
                ? 'bg-[var(--surface-raised)] text-[var(--text)] shadow-sm'
                : 'text-[var(--text-faint)] hover:text-[var(--text)]'
            }`}
          >
            <Columns2 size={15} />
          </button>
          <button
            type="button"
            title={t('editor.modePreview')}
            onClick={() => onViewMode('preview')}
            className={`rounded-md p-1.5 ${
              viewMode === 'preview'
                ? 'bg-[var(--surface-raised)] text-[var(--text)] shadow-sm'
                : 'text-[var(--text-faint)] hover:text-[var(--text)]'
            }`}
          >
            <Eye size={15} />
          </button>
        </div>

        <button
          type="button"
          title={t('editor.outline')}
          onClick={() => {
            setOutlineOpen((value) => !value);
            setShowBacklinks(false);
          }}
          className={`rounded-lg p-1.5 ${
            outlineOpen
              ? 'bg-[var(--accent-soft)] text-[var(--accent-strong)]'
              : 'text-[var(--text-muted)] hover:bg-[var(--surface-hover)]'
          }`}
        >
          <ListTree size={15} />
        </button>

        <button
          type="button"
          title={t('editor.settings')}
          onClick={() => setSettingsOpen((value) => !value)}
          className={`rounded-lg p-1.5 ${
            settingsOpen
              ? 'bg-[var(--accent-soft)] text-[var(--accent-strong)]'
              : 'text-[var(--text-muted)] hover:bg-[var(--surface-hover)]'
          }`}
        >
          <Settings2 size={15} />
        </button>

        <button
          type="button"
          title={t('editor.presentation')}
          onClick={() => setPptOpen(true)}
          className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--surface-hover)]"
        >
          <Presentation size={15} />
        </button>

        <button
          type="button"
          title={t('editor.backlinks')}
          onClick={() => {
            setShowBacklinks((value) => !value);
            setOutlineOpen(false);
          }}
          className={`flex items-center gap-1 rounded-lg px-1.5 py-1.5 text-[11px] ${
            showBacklinks
              ? 'bg-[var(--accent-soft)] text-[var(--accent-strong)]'
              : 'text-[var(--text-muted)] hover:bg-[var(--surface-hover)]'
          }`}
        >
          <Link2 size={15} />
          <span className="tabular-nums">{backlinksQuery.data?.length ?? 0}</span>
        </button>

        <DropdownMenu.Root>
          <DropdownMenu.Trigger asChild>
            <button
              type="button"
              title={t('editor.theme', {
                name: t(THEME_ITEMS.find((item) => item.value === theme)?.key ?? 'theme.system'),
              })}
              className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--surface-hover)]"
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
                  <span className="flex-1">{t(item.key)}</span>
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
              title={t('editor.more')}
              className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--surface-hover)]"
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
                <Pencil size={13} /> {t('common.rename')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => onPin(note, !note.pinned)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                {note.pinned ? <PinOff size={13} /> : <Pin size={13} />}
                {note.pinned ? t('notes.unpin') : t('notes.pin')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => onDuplicate(note)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <Copy size={13} /> {t('notes.duplicate')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => setVersionsOpen(true)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <History size={13} /> {t('editor.versions')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => {
                  void navigator.clipboard
                    .writeText(contentRef.current)
                    .then(() => toast(t('editor.copyMarkdown'), 'success'))
                    .catch(() => toast(t('editor.copyFailed'), 'error'));
                }}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <Copy size={13} /> {t('editor.copyAll')}
              </DropdownMenu.Item>
              <DropdownMenu.Separator className="my-1 h-px bg-[var(--border)]" />
              <DropdownMenu.Item
                onSelect={() => ocrInputRef.current?.click()}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <ScanText size={13} /> {t('editor.ocr')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => transcribeInputRef.current?.click()}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <AudioLines size={13} /> {t('editor.transcribe')}
              </DropdownMenu.Item>
              <DropdownMenu.Separator className="my-1 h-px bg-[var(--border)]" />
              <DropdownMenu.Item
                onSelect={() => exportMarkdown(note.title, contentRef.current)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
              >
                <FileText size={13} /> {t('editor.exportMarkdown')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                disabled={exporting}
                onSelect={() => void handleExportHtml()}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)] data-[disabled]:opacity-50"
              >
                <FileCode2 size={13} /> {t('editor.exportHtml')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                disabled={exporting}
                onSelect={() => void handleExportPdf()}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)] data-[disabled]:opacity-50"
              >
                <Printer size={13} /> {t('editor.exportPdf')}
              </DropdownMenu.Item>
              <DropdownMenu.Item
                onSelect={() => setDeleting(true)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-[var(--danger)] outline-none data-[highlighted]:bg-[var(--danger-soft)]"
              >
                <Trash2 size={13} /> {t('editor.deleteNote')}
              </DropdownMenu.Item>
            </DropdownMenu.Content>
          </DropdownMenu.Portal>
        </DropdownMenu.Root>
      </header>

      {viewMode !== 'preview' && (
        <div className="format-bar flex h-9 shrink-0 items-center gap-0.5 overflow-x-auto border-b border-[var(--border)] bg-[var(--surface)] px-2">
          <FormatButton title={t('format.h1')} onClick={() => editorRef.current?.toggleHeading(1)}>
            <Heading1 size={15} />
          </FormatButton>
          <FormatButton title={t('format.h2')} onClick={() => editorRef.current?.toggleHeading(2)}>
            <Heading2 size={15} />
          </FormatButton>
          <FormatButton title={t('format.h3')} onClick={() => editorRef.current?.toggleHeading(3)}>
            <Heading3 size={15} />
          </FormatButton>
          <FormatDivider />
          <FormatButton
            title={t('format.bold')}
            onClick={() => editorRef.current?.wrapSelection('**', '**', t('format.boldText'))}
          >
            <Bold size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.italic')}
            onClick={() => editorRef.current?.wrapSelection('*', '*', t('format.italicText'))}
          >
            <Italic size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.strike')}
            onClick={() => editorRef.current?.wrapSelection('~~', '~~', t('format.strikeText'))}
          >
            <Strikethrough size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.code')}
            onClick={() => editorRef.current?.wrapSelection('`', '`', t('format.codeText'))}
          >
            <Code2 size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.link')}
            onClick={() => editorRef.current?.wrapSelection('[', '](https://)', t('format.linkText'))}
          >
            <Link size={15} />
          </FormatButton>
          <FormatDivider />
          <FormatButton title={t('format.quote')} onClick={() => editorRef.current?.toggleLinePrefix('> ')}>
            <Quote size={15} />
          </FormatButton>
          <FormatButton title={t('format.ul')} onClick={() => editorRef.current?.toggleLinePrefix('- ')}>
            <List size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.ol')}
            onClick={() => editorRef.current?.toggleLinePrefix('1. ', true)}
          >
            <ListOrdered size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.todo')}
            onClick={() => editorRef.current?.toggleLinePrefix('- [ ] ')}
          >
            <ListTodo size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.table')}
            onClick={() => editorRef.current?.insertText(t('format.tableContent'))}
          >
            <Table2 size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.mermaid')}
            onClick={() => {
              const template = t('format.mermaidContent');
              editorRef.current?.insertTemplate(template, template.indexOf('\n') + 1);
            }}
          >
            <Workflow size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.math')}
            onClick={() => editorRef.current?.wrapSelection('$$\n', '\n$$', 'E = mc^2')}
          >
            <Sigma size={15} />
          </FormatButton>
          <FormatButton
            title={t('format.divider')}
            onClick={() => editorRef.current?.insertText('\n---\n')}
          >
            <Minus size={15} />
          </FormatButton>
          <span className="flex-1" />
          <FormatButton title={t('format.typography')} onClick={handleCleanTypography}>
            <SpellCheck size={15} />
          </FormatButton>
          <FormatButton title={t('format.tidy')} onClick={handleTidy}>
            <Wand2 size={15} />
          </FormatButton>
          <span className="hidden items-center gap-1 pr-1 text-[11px] text-[var(--text-faint)] md:flex">
            {t('format.templates')}
          </span>
        </div>
      )}

      {externalUpdate && (
        <div className="flex items-center gap-2 border-b border-[var(--border)] bg-[var(--warning-soft)] px-3 py-2 text-xs text-[var(--warning)]">
          <AlertTriangle size={13} />
          <span className="flex-1">{t('editor.externalUpdate')}</span>
          <button
            type="button"
            onClick={restoreFromDisk}
            className="rounded-md border border-current/30 px-2 py-1 hover:bg-white/20"
          >
            {t('editor.loadDisk')}
          </button>
        </div>
      )}

      {showBacklinks && (
        <div className="absolute right-3 top-[58px] z-30 flex max-h-[62%] w-[min(92vw,370px)] flex-col overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface-raised)] shadow-2xl">
          <div className="flex shrink-0 items-center gap-2 border-b border-[var(--border)] px-3 py-2">
            <Link2 size={13} className="text-[var(--accent)]" />
            <span className="text-xs font-semibold">{t('editor.backlinks')}</span>
            <span className="text-[11px] text-[var(--text-faint)]">
              {backlinksQuery.data?.length ?? 0}
            </span>
            <span className="flex-1" />
            <button
              type="button"
              aria-label={t('editor.backlinks')}
              onClick={() => setShowBacklinks(false)}
              className="rounded p-0.5 text-[var(--text-faint)] hover:text-[var(--text)]"
            >
              <X size={13} />
            </button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-1.5">
            {backlinksQuery.isLoading ? (
              <div className="grid place-items-center py-8">
                <Loader2 size={15} className="animate-spin text-[var(--text-faint)]" />
              </div>
            ) : (backlinksQuery.data?.length ?? 0) === 0 ? (
              <p className="px-3 py-6 text-center text-xs leading-relaxed text-[var(--text-faint)]">
                {t('backlinks.empty', { title: note.title })}
                <br />
                {t('backlinks.hint', { title: note.title })}
              </p>
            ) : (
              backlinksQuery.data!.map((backlink) => (
                <button
                  key={backlink.noteId}
                  type="button"
                  onClick={() => {
                    setShowBacklinks(false);
                    navigate(`/note/${backlink.noteId}`);
                  }}
                  className="mb-1 block w-full rounded-lg px-2.5 py-2 text-left hover:bg-[var(--surface-hover)]"
                >
                  <span className="flex items-center gap-1.5 text-xs font-medium">
                    <span className="truncate">{backlink.title}</span>
                    <span className="shrink-0 text-[10px] font-normal text-[var(--text-faint)]">
                      {backlink.projectName} · {t('backlinks.line', { line: backlink.line })}
                    </span>
                  </span>
                  <span className="mt-0.5 line-clamp-2 text-[11px] leading-relaxed text-[var(--text-muted)]">
                    {backlink.excerpt}
                  </span>
                </button>
              ))
            )}
          </div>
        </div>
      )}

      {outlineOpen && (
        <div className="absolute right-3 top-[58px] z-30 flex max-h-[62%] w-[min(92vw,320px)] flex-col overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface-raised)] shadow-2xl">
          <div className="flex shrink-0 items-center gap-2 border-b border-[var(--border)] px-3 py-2">
            <ListTree size={13} className="text-[var(--accent)]" />
            <span className="text-xs font-semibold">{t('editor.outline')}</span>
            <span className="text-[11px] text-[var(--text-faint)]">{headings.length}</span>
            <span className="flex-1" />
            <button
              type="button"
              aria-label={t('editor.outline')}
              onClick={() => setOutlineOpen(false)}
              className="rounded p-0.5 text-[var(--text-faint)] hover:text-[var(--text)]"
            >
              <X size={13} />
            </button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-1.5">
            {headings.length === 0 ? (
              <p className="px-3 py-6 text-center text-xs text-[var(--text-faint)]">
                {t('outline.empty')}
              </p>
            ) : (
              headings.map((heading) => (
                <button
                  key={`${heading.id}-${heading.line}`}
                  type="button"
                  onClick={() => {
                    previewRef.current?.scrollToHeading(heading.id);
                    editorRef.current?.scrollToLine(heading.line);
                  }}
                  style={{ paddingLeft: `${8 + (heading.level - 1) * 12}px` }}
                  className="block w-full truncate rounded-md py-1.5 pr-2 text-left text-xs text-[var(--text-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--text)]"
                  title={heading.text}
                >
                  {heading.text}
                </button>
              ))
            )}
          </div>
        </div>
      )}

      {settingsOpen && (
        <>
          <button
            type="button"
            aria-label={t('editor.settings')}
            onClick={() => setSettingsOpen(false)}
            className="fixed inset-0 z-20 cursor-default"
          />
          <div className="absolute right-3 top-[58px] z-30 w-[min(92vw,290px)] rounded-xl border border-[var(--border)] bg-[var(--surface-raised)] p-3 shadow-2xl">
            <div className="mb-2 flex items-center gap-2">
              <Settings2 size={13} className="text-[var(--accent)]" />
              <span className="text-xs font-semibold">{t('editor.settings')}</span>
              <span className="flex-1" />
              <button
                type="button"
                aria-label={t('editor.settings')}
                onClick={() => setSettingsOpen(false)}
                className="rounded p-0.5 text-[var(--text-faint)] hover:text-[var(--text)]"
              >
                <X size={13} />
              </button>
            </div>

            <label className="flex cursor-pointer items-center justify-between rounded-lg px-1 py-1.5 text-xs">
              <span className="flex items-center gap-2">
                <Focus size={13} /> {t('settings.focus')}
              </span>
              <input
                type="checkbox"
                checked={focusMode}
                onChange={(event) => setFocusMode(event.target.checked)}
                className="accent-[var(--accent)]"
              />
            </label>
            <label className="flex cursor-pointer items-center justify-between rounded-lg px-1 py-1.5 text-xs">
              <span>{t('settings.typewriter')}</span>
              <input
                type="checkbox"
                checked={typewriter}
                onChange={(event) => setTypewriter(event.target.checked)}
                className="accent-[var(--accent)]"
              />
            </label>

            <SettingRange
              label={t('settings.fontSize')}
              value={editorFontSize}
              min={12}
              max={22}
              step={1}
              suffix="px"
              onChange={setEditorFontSize}
            />
            <SettingRange
              label={t('settings.lineHeight')}
              value={editorLineHeight}
              min={1.4}
              max={2.4}
              step={0.05}
              onChange={setEditorLineHeight}
            />
            <SettingRange
              label={t('settings.width')}
              value={editorWidth}
              min={600}
              max={1100}
              step={20}
              suffix="px"
              onChange={setEditorWidth}
            />

            <div className="mt-1 flex items-center justify-between rounded-lg px-1 py-1.5 text-xs">
              <span className="text-[var(--text-muted)]">{t('settings.language')}</span>
              <div className="flex items-center gap-1">
                {(['zh', 'en'] as const).map((value) => (
                  <button
                    key={value}
                    type="button"
                    onClick={() => setLanguage(value)}
                    className={`rounded-md px-2 py-0.5 ${
                      language === value
                        ? 'bg-[var(--accent-soft)] font-medium text-[var(--accent-strong)]'
                        : 'text-[var(--text-faint)] hover:text-[var(--text)]'
                    }`}
                  >
                    {t(value === 'zh' ? 'settings.zh' : 'settings.en')}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </>
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
              onVisibleLine={syncFromEditor}
              typewriter={typewriter}
              fontSize={editorFontSize}
              lineHeight={editorLineHeight}
              contentWidth={editorWidth}
            />
          </div>
        )}

        {viewMode === 'split' && (
          <div className="w-px shrink-0 bg-[var(--border)]" aria-hidden />
        )}

        {viewMode !== 'editor' && (
          <div
            style={{ flex: viewMode === 'split' ? '1 1 50%' : '1 1 100%' }}
            className="min-w-0 bg-[var(--reading-surface)]"
          >
            <MarkdownPreview
              ref={previewRef}
              content={content}
              noteId={note.id}
              linkTitles={linkTitles}
              onWikilink={(title, unresolved) => void openWikilink(title, unresolved)}
              onVisibleLine={syncFromPreview}
            />
          </div>
        )}
      </div>

      <ConfirmDialog
        open={deleting}
        title={t('delete.title', { title: note.title })}
        description={t('delete.description')}
        confirmText={t('delete.confirm')}
        onOpenChange={setDeleting}
        onConfirm={() => {
          deletingRef.current = true;
          return onDelete(note);
        }}
      />

      <VersionHistoryDialog
        noteId={note.id}
        open={versionsOpen}
        onOpenChange={setVersionsOpen}
        currentContent={content}
        currentEtag={etagRef.current ?? note.etag}
        onRestored={handleVersionRestored}
      />

      {pptOpen && (
        <PptView
          content={content}
          noteId={note.id}
          title={note.title}
          linkTitles={linkTitles}
          onWikilink={(title, unresolved) => {
            setPptOpen(false);
            void openWikilink(title, unresolved);
          }}
          onClose={() => setPptOpen(false)}
        />
      )}

      <ConflictDialog
        conflict={conflict}
        onLoadDisk={restoreFromDisk}
        onKeepMine={() => void keepMine()}
        onClose={() => setConflict(null)}
      />

      <input
        ref={ocrInputRef}
        type="file"
        accept="image/*"
        hidden
        onChange={(event) => {
          void runMediaTool('ocr', event.target.files);
          event.target.value = '';
        }}
      />
      <input
        ref={transcribeInputRef}
        type="file"
        accept="audio/*,video/*"
        hidden
        onChange={(event) => {
          void runMediaTool('transcribe', event.target.files);
          event.target.value = '';
        }}
      />
    </section>
  );
}

function FormatButton({
  title,
  onClick,
  children,
}: {
  title: string;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      onClick={onClick}
      className="shrink-0 rounded-md p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent-strong)]"
    >
      {children}
    </button>
  );
}

function FormatDivider() {
  return <span className="mx-1 h-4 w-px shrink-0 bg-[var(--border)]" aria-hidden />;
}

function SettingRange({
  label,
  value,
  min,
  max,
  step,
  suffix = '',
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  suffix?: string;
  onChange: (value: number) => void;
}) {
  return (
    <label className="mt-1 block rounded-lg px-1 py-1 text-xs">
      <span className="flex items-center justify-between">
        <span className="text-[var(--text-muted)]">{label}</span>
        <span className="tabular-nums text-[var(--text-faint)]">
          {Number.isInteger(value) ? value : value.toFixed(2)}
          {suffix}
        </span>
      </span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        className="mt-1 w-full accent-[var(--accent)]"
      />
    </label>
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
  const { t } = useI18n();
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
              <h3 id="conflict-title" className="text-base font-semibold">{t('conflict.title')}</h3>
              <p className="mt-1 text-sm leading-relaxed text-[var(--text-muted)]">
                {t('conflict.description')}
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
              {t('conflict.later')}
            </button>
            <button
              type="button"
              onClick={onLoadDisk}
              className="rounded-lg border border-[var(--border-strong)] px-3.5 py-2 text-sm hover:bg-[var(--accent-soft)]"
            >
              {t('conflict.loadDisk')}
            </button>
            <button
              type="button"
              onClick={onKeepMine}
              className="rounded-lg bg-[var(--accent)] px-3.5 py-2 text-sm font-medium text-white"
            >
              {t('conflict.keepMine')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
