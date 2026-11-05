import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import { Feather, FolderPlus, Layers, MoreHorizontal, Pencil, Trash2 } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import type { Project } from '../api/types';
import { useUiStore } from '../store/ui';
import { ConfirmDialog, PromptDialog } from './Dialogs';

interface Props {
  projects: Project[] | undefined;
  onRename: (project: Project, name: string) => Promise<void>;
  onDelete: (project: Project) => Promise<void>;
  onCreate: (name: string) => Promise<void>;
  onOpenTrash: () => void;
}

interface RowProps {
  id: string;
  name: string;
  count?: number;
  active: boolean;
  icon: ReactNode;
  onSelect: () => void;
  actions?: ReactNode;
}

function Row({ name, count, active, icon, onSelect, actions }: RowProps) {
  return (
    <div
      className={`group flex items-center gap-2 rounded-lg px-2.5 py-1.5 text-[13px] ${
        active
          ? 'bg-[var(--accent-soft)] font-medium text-[var(--accent-strong)]'
          : 'text-[var(--text-muted)] hover:bg-[var(--accent-soft)] hover:text-[var(--text)]'
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
      {count !== undefined && (
        <span className="shrink-0 text-[11px] tabular-nums text-[var(--text-faint)] group-hover:hidden">
          {count > 0 ? count : ''}
        </span>
      )}
    </div>
  );
}

export function ProjectSidebar({ projects, onRename, onDelete, onCreate, onOpenTrash }: Props) {
  const selectedProjectId = useUiStore((state) => state.selectedProjectId);
  const setProject = useUiStore((state) => state.setProject);

  const [creating, setCreating] = useState(false);
  const [renaming, setRenaming] = useState<Project | null>(null);
  const [deleting, setDeleting] = useState<Project | null>(null);

  const totalNotes = useMemo(
    () => (projects ?? []).reduce((sum, project) => sum + project.noteCount, 0),
    [projects],
  );

  return (
    <aside className="flex h-full w-full flex-col overflow-hidden border-r border-[var(--border)] bg-[var(--surface)]">
      <div className="flex h-[52px] shrink-0 items-center gap-2 border-b border-[var(--border)] px-3.5">
        <span className="grid size-7 place-items-center rounded-lg bg-[var(--accent)] text-white">
          <Feather size={14} strokeWidth={2} />
        </span>
        <span className="text-[13.5px] font-semibold tracking-tight">妙言 Web</span>
      </div>

      <div className="flex items-center justify-between px-3.5 pb-2 pt-3.5">
        <span className="text-[11px] font-semibold uppercase tracking-[0.14em] text-[var(--text-faint)]">
          书库
        </span>
        <button
          type="button"
          title="新建项目"
          onClick={() => setCreating(true)}
          className="rounded-md p-1 text-[var(--text-faint)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent-strong)]"
        >
          <FolderPlus size={15} />
        </button>
      </div>

      <nav className="flex-1 space-y-0.5 overflow-y-auto px-2 pb-3">
        <Row
          id="all"
          name="全部笔记"
          count={totalNotes}
          active={selectedProjectId === 'all'}
          icon={<Layers size={14} />}
          onSelect={() => setProject('all')}
        />
        <Row
          id="root"
          name="未分类"
          active={selectedProjectId === 'root'}
          icon={<FolderPlus size={14} />}
          onSelect={() => setProject('root')}
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
              icon={<span className="block size-2.5 rounded-[3px] border border-current" />}
              onSelect={() => setProject(project.id)}
              actions={
                <DropdownMenu.Root>
                  <DropdownMenu.Trigger asChild>
                    <button
                      type="button"
                      aria-label={`${project.name} 的操作`}
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
                        <Pencil size={13} /> 重命名项目
                      </DropdownMenu.Item>
                      <DropdownMenu.Item
                        onSelect={() => setDeleting(project)}
                        className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-[var(--danger)] outline-none data-[highlighted]:bg-[var(--danger-soft)]"
                      >
                        <Trash2 size={13} /> 删除项目
                      </DropdownMenu.Item>
                    </DropdownMenu.Content>
                  </DropdownMenu.Portal>
                </DropdownMenu.Root>
              }
            />
          ))}

        {(projects ?? []).filter((project) => project.kind === 'folder').length === 0 && (
          <p className="px-3 py-2 text-[12px] leading-relaxed text-[var(--text-faint)]">
            还没有项目。点击右上角 + 新建一个，笔记也可以直接放在「未分类」。
          </p>
        )}
      </nav>

      <div className="border-t border-[var(--border)] p-2">
        <button
          type="button"
          onClick={onOpenTrash}
          className="flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-[13px] text-[var(--text-muted)] hover:bg-[var(--accent-soft)] hover:text-[var(--text)]"
        >
          <Trash2 size={14} className="text-[var(--text-faint)]" />
          回收站
        </button>
      </div>

      <PromptDialog
        open={creating}
        title="新建项目"
        label="项目名称"
        placeholder="例如：工作笔记"
        confirmText="创建"
        onOpenChange={setCreating}
        onSubmit={onCreate}
      />

      <PromptDialog
        open={renaming !== null}
        title="重命名项目"
        label="项目名称"
        initialValue={renaming?.name ?? ''}
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
        title={`删除「${deleting?.name ?? ''}」？`}
        description={
          <>
            项目中的 {deleting?.noteCount ?? 0} 篇笔记会一起移入回收站，可以随时恢复。
          </>
        }
        confirmText="移入回收站"
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
