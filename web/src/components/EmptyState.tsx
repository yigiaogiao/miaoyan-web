import { Feather, Keyboard, Plus } from 'lucide-react';

interface Props {
  onCreate: () => void;
}

export function EmptyState({ onCreate }: Props) {
  return (
    <div className="grid h-full place-items-center bg-[var(--surface)] px-8">
      <div className="max-w-sm text-center">
        <div className="mx-auto mb-5 grid size-14 place-items-center rounded-2xl bg-[var(--accent-soft)] text-[var(--accent-strong)]">
          <Feather size={26} strokeWidth={1.6} />
        </div>
        <h2 className="text-lg font-semibold">选择一篇笔记开始</h2>
        <p className="mt-1.5 text-sm leading-relaxed text-[var(--text-muted)]">
          从左侧列表打开笔记，或者新建一篇。所有内容都以 Markdown 文件保存在书库中。
        </p>
        <button
          type="button"
          onClick={onCreate}
          className="mt-5 inline-flex items-center gap-1.5 rounded-xl bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white shadow-sm hover:brightness-105"
        >
          <Plus size={15} /> 新建笔记
        </button>
        <div className="mt-6 flex items-center justify-center gap-1.5 text-[12px] text-[var(--text-faint)]">
          <Keyboard size={13} />
          <span>⌘N 新建 · ⌘S 保存 · ⌘\ 切换分栏</span>
        </div>
      </div>
    </div>
  );
}
