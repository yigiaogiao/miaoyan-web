import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import {
  Download,
  Eraser,
  Folder,
  FolderPlus,
  Inbox,
  Layers,
  MoreHorizontal,
  MoveRight,
  Pencil,
  Trash2,
} from 'lucide-react';
import { useEffect, useMemo, useState, type DragEvent, type ReactNode } from 'react';
import type { Project } from '../api/types';
import { useI18n } from '../lib/i18n';
import { useUiStore } from '../store/ui';
import { AppIcon } from './AppIcon';
import { ConfirmDialog, PromptDialog } from './Dialogs';

const NOTE_DRAG_TYPE = 'application/x-miaoyan-note';

interface Props {
  projects: Project[] | undefined;
  onRename: (project: Project, name: string) => Promise<void>;
  onDelete: (project: Project) => Promise<void>;
  onCreate: (name: string) => Promise<void>;
  onOpenTrash: () => void;
  onCleanUnused: () => void;
  onMoveNote: (noteId: string, projectId: string) => void;
}

interface RowProps {
  id: string;
  name: string;
  count?: number;
  active: boolean;
  icon: ReactNode;
  onSelect: () => void;
  onDropNote?: (noteId: string) => void;
  actions?: ReactNode;
  dragging?: boolean;
}

function Row({
  id,
  name,
  count,
  active,
  icon,
  onSelect,
  onDropNote,
  actions,
  dragging = false,
}: RowProps) {
  const [dragOver, setDragOver] = useState(false);

  // Cancelling a drag (Esc, drop outside) fires no dragleave on the row, so
  // clear the highlight when the global drag state ends.
  useEffect(() => {
    if (!dragging) {
      setDragOver(false);
    }
  }, [dragging]);

  function onDragOver(event: DragEvent<HTMLDivElement>) {
    if (!onDropNote || !event.dataTransfer.types.includes(NOTE_DRAG_TYPE)) {
      return;
    }
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    setDragOver(true);
  }

  function onDragLeave(event: DragEvent<HTMLDivElement>) {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
      setDragOver(false);
    }
  }

  function onDrop(event: DragEvent<HTMLDivElement>) {
    if (!onDropNote) {
      return;
    }
    event.preventDefault();
    setDragOver(false);
    const noteId = event.dataTransfer.getData(NOTE_DRAG_TYPE);
    if (noteId) {
      onDropNote(noteId);
    }
  }

  return (
    <div
      data-project-id={id}
      onDragOver={onDragOver}
      onDragEnter={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      className={`group flex items-center gap-2 rounded-lg px-2.5 py-[7px] text-[13px] transition-colors ${
        dragOver
          ? 'bg-[var(--accent-soft)] font-medium text-[var(--accent-strong)] ring-2 ring-inset ring-[var(--accent)]'
          : active
            ? 'bg-[var(--selection-row)] font-medium text-[var(--text)]'
            : dragging && onDropNote
              ? 'text-[var(--text)] outline outline-1 -outline-offset-1 outline-dashed outline-[var(--border-strong)]'
              : 'text-[var(--text-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--text)]'
      }`}
    >
      <button
        type="button"
        onClick={onSelect}
        className="flex min-w-0 flex-1 items-center gap-2 text-left"
      >
        <span className="shrink-0 text-[var(--text-faint)]">{icon}</span>
        <span className="truncate">{name}</span>
      </button>
      {actions}
      {dragOver ? (
        <MoveRight size={13} className="shrink-0 text-[var(--accent-strong)]" aria-hidden />
      ) : (
        count !== undefined && (
          <span className="shrink-0 text-[11px] tabular-nums text-[var(--text-faint)] group-hover:hidden">
            {count > 0 ? count : ''}
          </span>
        )
      )}
    </div>
  );
}

export function ProjectSidebar({
  projects,
  onRename,
  onDelete,
  onCreate,
  onOpenTrash,
  onCleanUnused,
  onMoveNote,
}: Props) {
  const { t } = useI18n();
  const selectedProjectId = useUiStore((state) => state.selectedProjectId);
  const setProject = useUiStore((state) => state.setProject);
  const projectCreateRequest = useUiStore((state) => state.projectCreateRequest);

  const [creating, setCreating] = useState(false);
  const [noteDragActive, setNoteDragActive] = useState(false);

  useEffect(() => {
    if (projectCreateRequest > 0) {
      setCreating(true);
    }
  }, [projectCreateRequest]);

  // Highlight every droppable folder while a note is being dragged, so the
  // valid targets are visible before the pointer reaches them.
  useEffect(() => {
    function onDragStart(event: globalThis.DragEvent) {
      if (event.dataTransfer?.types.includes(NOTE_DRAG_TYPE)) {
        setNoteDragActive(true);
      }
    }
    const stop = () => setNoteDragActive(false);
    window.addEventListener('dragstart', onDragStart);
    window.addEventListener('dragend', stop);
    window.addEventListener('drop', stop);
    return () => {
      window.removeEventListener('dragstart', onDragStart);
      window.removeEventListener('dragend', stop);
      window.removeEventListener('drop', stop);
    };
  }, []);
  const [renaming, setRenaming] = useState<Project | null>(null);
  const [deleting, setDeleting] = useState<Project | null>(null);

  const totalNotes = useMemo(
    () => (projects ?? []).reduce((sum, project) => sum + project.noteCount, 0),
    [projects],
  );
  const rootCount = useMemo(
    () => (projects ?? []).find((project) => project.kind === 'root')?.noteCount ?? 0,
    [projects],
  );

  return (
    <aside className="flex h-full w-full flex-col overflow-hidden border-r border-[var(--border)] bg-[var(--surface)]">
      <div className="flex h-[52px] shrink-0 items-center gap-2.5 border-b border-[var(--border)] px-3.5">
        <AppIcon className="size-6 shrink-0" />
        <span className="font-[family-name:var(--font-brand)] text-[15px] tracking-tight text-[var(--text)]">
          {t('brand')}
        </span>
      </div>

      <div className="flex items-center justify-between px-3.5 pb-2 pt-3.5">
        <span className="text-[11px] font-semibold uppercase tracking-[0.14em] text-[var(--text-faint)]">
          {t('sidebar.library')}
        </span>
        <button
          type="button"
          title={t('sidebar.newProject')}
          onClick={() => setCreating(true)}
          className="rounded-md p-1 text-[var(--text-faint)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent-strong)]"
        >
          <FolderPlus size={15} />
        </button>
      </div>

      <nav className="flex-1 space-y-0.5 overflow-y-auto px-2 pb-3">
        <Row
          id="all"
          name={t('sidebar.allNotes')}
          count={totalNotes}
          active={selectedProjectId === 'all'}
          icon={<Layers size={14} strokeWidth={1.75} />}
          onSelect={() => setProject('all')}
        />
        <Row
          id="root"
          name={t('sidebar.uncategorized')}
          count={rootCount}
          active={selectedProjectId === 'root'}
          icon={<Inbox size={14} strokeWidth={1.75} />}
          onSelect={() => setProject('root')}
          onDropNote={(noteId) => onMoveNote(noteId, 'root')}
          dragging={noteDragActive}
        />

        {(projects ?? [])
          .filter((project) => project.kind === 'folder')
          .map((project) => (
            <Row
              key={project.id}
              id={project.id}
              name={project.name}
              count={project.noteCount}
              active={selectedProjectId === project.id}
              icon={<Folder size={14} strokeWidth={1.75} />}
              onSelect={() => setProject(project.id)}
              onDropNote={(noteId) => onMoveNote(noteId, project.id)}
              dragging={noteDragActive}
              actions={
                <DropdownMenu.Root>
                  <DropdownMenu.Trigger asChild>
                    <button
                      type="button"
                      aria-label={t('sidebar.projectActions', { name: project.name })}
                      className="hidden rounded p-0.5 text-[var(--text-faint)] hover:text-[var(--text)] group-hover:block data-[state=open]:block"
                    >
                      <MoreHorizontal size={14} />
                    </button>
                  </DropdownMenu.Trigger>
                  <DropdownMenu.Portal>
                    <DropdownMenu.Content
                      align="start"
                      sideOffset={4}
                      className="z-50 min-w-36 rounded-lg border border-[var(--border)] bg-[var(--surface-raised)] p-1 text-[13px] shadow-xl"
                    >
                      <DropdownMenu.Item
                        onSelect={() => setRenaming(project)}
                        className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 outline-none data-[highlighted]:bg-[var(--accent-soft)]"
                      >
                        <Pencil size={13} /> {t('sidebar.renameProject')}
                      </DropdownMenu.Item>
                      <DropdownMenu.Item
                        onSelect={() => setDeleting(project)}
                        className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-[var(--danger)] outline-none data-[highlighted]:bg-[var(--danger-soft)]"
                      >
                        <Trash2 size={13} /> {t('sidebar.deleteProject')}
                      </DropdownMenu.Item>
                    </DropdownMenu.Content>
                  </DropdownMenu.Portal>
                </DropdownMenu.Root>
              }
            />
          ))}

        {(projects ?? []).filter((project) => project.kind === 'folder').length === 0 && (
          <p className="px-3 py-2 text-[12px] leading-relaxed text-[var(--text-faint)]">
            {t('sidebar.empty')}
          </p>
        )}
      </nav>

      <div className="space-y-0.5 border-t border-[var(--border)] p-2">
        <a
          href="/api/export/vault"
          className="flex w-full items-center gap-2 rounded-lg px-2.5 py-[7px] text-[13px] text-[var(--text-muted)] transition-colors hover:bg-[var(--surface-hover)] hover:text-[var(--text)]"
        >
          <Download size={14} className="text-[var(--text-faint)]" />
          {t('sidebar.exportVault')}
        </a>
        <button
          type="button"
          onClick={onCleanUnused}
          className="flex w-full items-center gap-2 rounded-lg px-2.5 py-[7px] text-[13px] text-[var(--text-muted)] transition-colors hover:bg-[var(--surface-hover)] hover:text-[var(--text)]"
        >
          <Eraser size={14} className="text-[var(--text-faint)]" />
          {t('sidebar.cleanUnused')}
        </button>
        <button
          type="button"
          onClick={onOpenTrash}
          className="flex w-full items-center gap-2 rounded-lg px-2.5 py-[7px] text-[13px] text-[var(--text-muted)] transition-colors hover:bg-[var(--surface-hover)] hover:text-[var(--text)]"
        >
          <Trash2 size={14} className="text-[var(--text-faint)]" />
          {t('sidebar.trash')}
        </button>
      </div>

      <PromptDialog
        open={creating}
        title={t('sidebar.newProject')}
        label={t('sidebar.projectName')}
        placeholder={t('sidebar.projectPlaceholder')}
        confirmText={t('common.create')}
        onOpenChange={setCreating}
        onSubmit={onCreate}
      />

      <PromptDialog
        open={renaming !== null}
        title={t('sidebar.renameProject')}
        label={t('sidebar.projectName')}
        initialValue={renaming?.name ?? ''}
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
        title={t('sidebar.deleteTitle', { name: deleting?.name ?? '' })}
        description={t('sidebar.deleteDescription', { count: deleting?.noteCount ?? 0 })}
        confirmText={t('notes.moveToTrash')}
        onOpenChange={(open) => !open && setDeleting(null)}
        onConfirm={async () => {
          if (deleting) {
            await onDelete(deleting);
          }
        }}
      />
    </aside>
  );
}
