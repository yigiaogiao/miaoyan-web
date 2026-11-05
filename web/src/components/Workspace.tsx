import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import { useNavigate, useParams } from 'react-router';
import { api } from '../api/client';
import type { ChangeEvent, Note, NoteSummary, Project, TrashRestoreResult } from '../api/types';
import { isMod } from '../lib/utils';
import { useToastStore } from '../store/toast';
import { useUiStore } from '../store/ui';
import { CommandPalette } from './CommandPalette';
import { PromptDialog } from './Dialogs';
import { EditorPane } from './EditorPane';
import { EmptyState } from './EmptyState';
import { NoteList } from './NoteList';
import { ProjectSidebar } from './ProjectSidebar';
import { Toaster } from './Toaster';
import { TrashDialog } from './TrashDialog';

export function Workspace() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { noteId = null } = useParams();
  const toast = useToastStore((state) => state.push);

  const selectedProjectId = useUiStore((state) => state.selectedProjectId);
  const setProject = useUiStore((state) => state.setProject);
  const viewMode = useUiStore((state) => state.viewMode);
  const setViewMode = useUiStore((state) => state.setViewMode);
  const sidebarOpen = useUiStore((state) => state.sidebarOpen);
  const listOpen = useUiStore((state) => state.listOpen);
  const toggleSidebar = useUiStore((state) => state.toggleSidebar);
  const toggleList = useUiStore((state) => state.toggleList);
  const setPanels = useUiStore((state) => state.setPanels);
  const sort = useUiStore((state) => state.sort);
  const setSort = useUiStore((state) => state.setSort);
  const sidebarWidth = useUiStore((state) => state.sidebarWidth);
  const listWidth = useUiStore((state) => state.listWidth);
  const setSidebarWidth = useUiStore((state) => state.setSidebarWidth);
  const setListWidth = useUiStore((state) => state.setListWidth);
  const requestCreateProject = useUiStore((state) => state.requestCreateProject);

  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [renamingNote, setRenamingNote] = useState<Note | null>(null);
  const [trashOpen, setTrashOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [isNarrow, setIsNarrow] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 860px)').matches,
  );

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSearch(search.trim()), 220);
    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    const query = window.matchMedia('(max-width: 860px)');
    function onChange() {
      setIsNarrow(query.matches);
      if (query.matches) {
        setPanels(false, false);
      }
    }
    if (query.matches) {
      setPanels(false, false);
    }
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, [setPanels]);

  const projectsQuery = useQuery({
    queryKey: ['projects'],
    queryFn: api.projects,
  });

  const notesQuery = useQuery({
    queryKey: ['notes', selectedProjectId, debouncedSearch],
    queryFn: () => api.notes(selectedProjectId, debouncedSearch),
    placeholderData: (previous) => previous,
  });

  const notes = notesQuery.data;
  const sortedNotes = useMemo(() => {
    if (!notes) {
      return notes;
    }
    const copy = [...notes];
    copy.sort((left, right) => {
      if (left.pinned !== right.pinned) {
        return left.pinned ? -1 : 1;
      }
      if (sort === 'title') {
        return left.title.localeCompare(right.title, 'zh-CN');
      }
      if (sort === 'created') {
        return right.createdAt.localeCompare(left.createdAt);
      }
      return right.modifiedAt.localeCompare(left.modifiedAt);
    });
    return copy;
  }, [notes, sort]);

  const autoOpened = useRef(false);
  useEffect(() => {
    if (autoOpened.current || noteId || !notes || notes.length === 0) {
      return;
    }
    autoOpened.current = true;
    navigate(`/note/${notes[0].id}`, { replace: true });
  }, [notes, noteId, navigate]);

  const currentNoteIdRef = useRef(noteId);
  currentNoteIdRef.current = noteId;

  useEffect(() => {
    const source = new EventSource(api.eventsUrl());

    source.addEventListener('change', (raw) => {
      let event: ChangeEvent | null = null;
      try {
        event = JSON.parse((raw as MessageEvent<string>).data) as ChangeEvent;
      } catch {
        event = null;
      }

      void queryClient.invalidateQueries({ queryKey: ['notes'] });
      void queryClient.invalidateQueries({ queryKey: ['projects'] });

      if (event?.noteId && event.noteId === currentNoteIdRef.current) {
        if (event.kind === 'deleted') {
          toast('当前笔记已在磁盘上被删除', 'error');
        }
        void queryClient.invalidateQueries({ queryKey: ['note', event.noteId] });
      }
    });

    return () => source.close();
  }, [queryClient, toast]);

  async function createNote() {
    const projectId = selectedProjectId === 'all' ? 'root' : selectedProjectId;
    try {
      const note = await api.createNote(projectId);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['notes'] }),
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
      ]);
      autoOpened.current = true;
      navigate(`/note/${note.id}`);
    } catch (reason) {
      toast(reason instanceof Error ? reason.message : '新建笔记失败', 'error');
    }
  }

  async function renameNote(note: NoteSummary, title: string) {
    const updated = await api.renameNote(note.id, title, null);
    queryClient.setQueryData<Note>(['note', updated.id], updated);
    if (updated.id !== note.id) {
      queryClient.removeQueries({ queryKey: ['note', note.id], exact: true });
    }
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['notes'] }),
      queryClient.invalidateQueries({ queryKey: ['projects'] }),
    ]);
    if (noteId === note.id && updated.id !== note.id) {
      navigate(`/note/${updated.id}`, { replace: true });
    }
  }

  async function moveNote(noteIdToMove: string, projectId: string) {
    const target = projectsQuery.data?.find((project) => project.id === projectId);
    try {
      const updated = await api.renameNote(noteIdToMove, null, projectId);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['notes'] }),
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
        queryClient.invalidateQueries({ queryKey: ['links'] }),
      ]);
      if (noteId === noteIdToMove && updated.id !== noteIdToMove) {
        navigate(`/note/${updated.id}`, { replace: true });
      }
      toast(`已移动到「${target?.name ?? '未分类'}」`, 'success');
    } catch (reason) {
      toast(reason instanceof Error ? reason.message : '移动笔记失败', 'error');
    }
  }

  async function pinNote(note: NoteSummary, pinned: boolean) {
    try {
      const updated = await api.setPinned(note.id, pinned);
      queryClient.setQueryData<Note>(['note', note.id], (current) =>
        current ? { ...current, ...updated } : updated,
      );
      await queryClient.invalidateQueries({ queryKey: ['notes'] });
      toast(pinned ? '已置顶' : '已取消置顶', 'success');
    } catch (reason) {
      toast(reason instanceof Error ? reason.message : '置顶失败', 'error');
    }
  }

  async function duplicateNote(note: NoteSummary) {
    try {
      const copy = await api.duplicateNote(note.id);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['notes'] }),
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
      ]);
      toast(`已创建「${copy.title}」`, 'success');
      navigate(`/note/${copy.id}`);
    } catch (reason) {
      toast(reason instanceof Error ? reason.message : '创建副本失败', 'error');
    }
  }

  async function deleteNote(note: NoteSummary) {
    await api.deleteNote(note.id);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['notes'] }),
      queryClient.invalidateQueries({ queryKey: ['projects'] }),
      queryClient.invalidateQueries({ queryKey: ['trash'] }),
    ]);
    if (note.id === noteId) {
      navigate('/', { replace: true });
    }
    toast('已移入回收站', 'success');
  }

  async function createProject(name: string) {
    const project = await api.createProject(name);
    await queryClient.invalidateQueries({ queryKey: ['projects'] });
    setProject(project.id);
  }

  async function renameProject(project: Project, name: string) {
    const updated = await api.renameProject(project.id, name);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['projects'] }),
      queryClient.invalidateQueries({ queryKey: ['notes'] }),
    ]);
    if (selectedProjectId === project.id) {
      setProject(updated.id);
    }
    if (noteId) {
      const openNote = queryClient.getQueryData<Note>(['note', noteId]);
      if (openNote?.projectId === project.id) {
        navigate('/', { replace: true });
      }
    }
  }

  async function deleteProject(project: Project) {
    const openNote = noteId ? queryClient.getQueryData<Note>(['note', noteId]) : undefined;
    await api.deleteProject(project.id);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['projects'] }),
      queryClient.invalidateQueries({ queryKey: ['notes'] }),
      queryClient.invalidateQueries({ queryKey: ['trash'] }),
    ]);
    if (selectedProjectId === project.id) {
      setProject('all');
    }
    if (openNote?.projectId === project.id) {
      navigate('/', { replace: true });
    }
    toast('项目已移入回收站', 'success');
  }

  function onRestored(result: TrashRestoreResult) {
    void queryClient.invalidateQueries({ queryKey: ['notes'] });
    void queryClient.invalidateQueries({ queryKey: ['projects'] });
    if (result.noteId) {
      queryClient.removeQueries({ queryKey: ['note', result.noteId], exact: true });
      navigate(`/note/${result.noteId}`);
    }
  }

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (!isMod(event)) {
        return;
      }
      const key = event.key.toLowerCase();
      if (key === 'k') {
        event.preventDefault();
        setPaletteOpen((value) => !value);
        return;
      }
      if (paletteOpen) {
        return;
      }
      if (key === 'n' && event.shiftKey) {
        event.preventDefault();
        if (!sidebarOpen) {
          toggleSidebar();
        }
        requestCreateProject();
      } else if (key === 'n') {
        event.preventDefault();
        void createNote();
      } else if (key === 'd') {
        const current = noteId ? queryClient.getQueryData<Note>(['note', noteId]) : null;
        if (current) {
          event.preventDefault();
          void duplicateNote(current);
        }
      } else if (key === 'r') {
        const current = noteId ? queryClient.getQueryData<Note>(['note', noteId]) : null;
        if (current) {
          event.preventDefault();
          setRenamingNote(current);
        }
      } else if (key === '1') {
        event.preventDefault();
        toggleSidebar();
      } else if (key === '2') {
        event.preventDefault();
        toggleList();
      } else if (key === '\\') {
        event.preventDefault();
        setViewMode(viewMode === 'split' ? 'editor' : 'split');
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  });

  function beginResize(kind: 'sidebar' | 'list', event: ReactPointerEvent<HTMLDivElement>) {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = kind === 'sidebar' ? sidebarWidth : listWidth;
    const min = kind === 'sidebar' ? 168 : 232;
    const max = kind === 'sidebar' ? 340 : 520;

    function onMove(moveEvent: PointerEvent) {
      const next = Math.min(max, Math.max(min, startWidth + moveEvent.clientX - startX));
      if (kind === 'sidebar') {
        setSidebarWidth(next);
      } else {
        setListWidth(next);
      }
    }

    function onUp() {
      window.removeEventListener('pointermove', onMove);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    }

    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp, { once: true });
  }

  const overlayOpen = isNarrow && (sidebarOpen || listOpen);
  const effectiveViewMode = isNarrow && viewMode === 'split' ? 'editor' : viewMode;

  return (
    <div className="flex h-dvh w-full overflow-hidden bg-[var(--bg)]">
      {overlayOpen && (
        <button
          type="button"
          aria-label="收起面板"
          onClick={() => setPanels(false, false)}
          className="fixed inset-0 z-20 bg-black/25 backdrop-blur-[1px]"
        />
      )}

      {sidebarOpen && (
        <div
          className={isNarrow ? 'fixed inset-y-0 left-0 z-40 w-[250px] shadow-2xl' : 'shrink-0'}
          style={isNarrow ? undefined : { width: sidebarWidth }}
        >
          <ProjectSidebar
            projects={projectsQuery.data}
            onCreate={createProject}
            onRename={renameProject}
            onDelete={deleteProject}
            onOpenTrash={() => setTrashOpen(true)}
            onMoveNote={(id, projectId) => void moveNote(id, projectId)}
          />
        </div>
      )}
      {sidebarOpen && !isNarrow && (
        <div
          role="separator"
          aria-orientation="vertical"
          title="拖动调整项目栏宽度"
          onPointerDown={(event) => beginResize('sidebar', event)}
          className="group relative z-10 -mr-1 w-2 shrink-0 cursor-col-resize"
        >
          <span className="absolute inset-y-0 left-1/2 w-px -translate-x-1/2 bg-transparent transition-colors group-hover:bg-[var(--accent-border)]" />
        </div>
      )}

      {listOpen && (
        <div
          className={
            isNarrow ? 'fixed inset-y-0 left-0 z-30 w-[min(86vw,330px)] shadow-2xl' : 'shrink-0'
          }
          style={isNarrow ? undefined : { width: listWidth }}
        >
          <NoteList
            notes={sortedNotes}
            loading={notesQuery.isLoading}
            activeNoteId={noteId}
            search={search}
            sort={sort}
            onSearch={setSearch}
            onSort={setSort}
            onOpen={(id) => navigate(`/note/${id}`)}
            onCreate={createNote}
            onRename={renameNote}
            onDelete={deleteNote}
            onPin={pinNote}
            onDuplicate={duplicateNote}
          />
        </div>
      )}
      {listOpen && !isNarrow && (
        <div
          role="separator"
          aria-orientation="vertical"
          title="拖动调整笔记列表宽度"
          onPointerDown={(event) => beginResize('list', event)}
          className="group relative z-10 -mr-1 w-2 shrink-0 cursor-col-resize"
        >
          <span className="absolute inset-y-0 left-1/2 w-px -translate-x-1/2 bg-transparent transition-colors group-hover:bg-[var(--accent-border)]" />
        </div>
      )}

      <main className="flex min-w-0 flex-1 flex-col">
        {noteId ? (
          <EditorPane
            key={noteId}
            noteId={noteId}
            viewMode={effectiveViewMode}
            onViewMode={setViewMode}
            onRename={setRenamingNote}
            onDelete={deleteNote}
            onPin={pinNote}
            onDuplicate={duplicateNote}
          />
        ) : (
          <EmptyState onCreate={createNote} />
        )}
      </main>

      <PromptDialog
        open={renamingNote !== null}
        title="重命名笔记"
        label="标题"
        initialValue={renamingNote?.title ?? ''}
        confirmText="保存"
        onOpenChange={(open) => !open && setRenamingNote(null)}
        onSubmit={async (value) => {
          if (renamingNote) {
            await renameNote(renamingNote, value);
          }
        }}
      />

      <TrashDialog open={trashOpen} onOpenChange={setTrashOpen} onRestored={onRestored} />

      <CommandPalette
        open={paletteOpen}
        onOpenChange={setPaletteOpen}
        onOpenNote={(id) => navigate(`/note/${id}`)}
        recent={sortedNotes}
      />

      <Toaster />
    </div>
  );
}
