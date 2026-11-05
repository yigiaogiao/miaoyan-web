import { useQuery } from '@tanstack/react-query';
import { Feather, HardDrive, Server } from 'lucide-react';

type Health = {
  status: string;
  version: string;
  uptimeSeconds: number;
  vault: {
    mode: string;
    documentsRoot: string | null;
    explicitLibrary: string | null;
    state: string;
    userCount: number;
  };
  sqlite: { version: string; fts5: boolean; trigram: boolean };
  capabilities: { plantuml: boolean; whisper: boolean; ocr: boolean; ffmpeg: boolean };
};

async function fetchHealth(): Promise<Health> {
  const response = await fetch('/healthz', { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(`健康检查失败 (${response.status})`);
  }
  return response.json() as Promise<Health>;
}

function Check({ value }: { value: boolean }) {
  return (
    <span className={value ? 'text-[var(--accent)]' : 'text-[var(--text-faint)]'}>
      {value ? '可用' : '未启用'}
    </span>
  );
}

export default function App() {
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth, refetchInterval: 30_000 });

  return (
    <main className="flex min-h-dvh items-center justify-center p-6">
      <section className="w-full max-w-2xl rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-8 shadow-sm">
        <div className="mb-6 flex items-center gap-3">
          <span className="grid size-10 place-items-center rounded-xl bg-[var(--accent-soft)] text-[var(--accent)]">
            <Feather size={22} strokeWidth={1.8} />
          </span>
          <div>
            <h1 className="text-xl font-semibold tracking-tight">妙言 Web</h1>
            <p className="text-sm text-[var(--text-muted)]">M0 · 服务骨架已就绪</p>
          </div>
        </div>

        {health.isPending && <p className="text-sm text-[var(--text-muted)]">正在连接服务…</p>}
        {health.isError && (
          <p className="rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
            {(health.error as Error).message}
          </p>
        )}

        {health.data && (
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div className="flex items-center gap-2">
              <Server size={15} className="text-[var(--text-faint)]" />
              <dt className="text-[var(--text-muted)]">版本</dt>
              <dd className="ml-auto font-mono">{health.data.version}</dd>
            </div>
            <div className="flex items-center gap-2">
              <HardDrive size={15} className="text-[var(--text-faint)]" />
              <dt className="text-[var(--text-muted)]">书库模式</dt>
              <dd className="ml-auto font-mono">{health.data.vault.mode}</dd>
            </div>
            <div className="sm:col-span-2">
              <dt className="text-[var(--text-muted)]">状态目录</dt>
              <dd className="mt-0.5 truncate font-mono text-xs">{health.data.vault.state}</dd>
            </div>
            <div className="sm:col-span-2">
              <dt className="text-[var(--text-muted)]">SQLite</dt>
              <dd className="mt-0.5 font-mono text-xs">
                {health.data.sqlite.version} · FTS5 <Check value={health.data.sqlite.fts5} /> · trigram{' '}
                <Check value={health.data.sqlite.trigram} />
              </dd>
            </div>
            <div className="sm:col-span-2">
              <dt className="text-[var(--text-muted)]">可选能力</dt>
              <dd className="mt-0.5 flex flex-wrap gap-x-4 gap-y-1 text-xs">
                <span>PlantUML <Check value={health.data.capabilities.plantuml} /></span>
                <span>Whisper <Check value={health.data.capabilities.whisper} /></span>
                <span>OCR <Check value={health.data.capabilities.ocr} /></span>
                <span>ffmpeg <Check value={health.data.capabilities.ffmpeg} /></span>
              </dd>
            </div>
          </dl>
        )}

        <p className="mt-8 border-t border-[var(--border)] pt-4 text-xs leading-relaxed text-[var(--text-faint)]">
          下一步：M1 三栏书库、CodeMirror 编辑器、自动保存与实时预览。此页面仅用于确认容器与服务健康。
        </p>
      </section>
    </main>
  );
}
