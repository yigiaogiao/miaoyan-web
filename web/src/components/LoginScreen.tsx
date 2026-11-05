import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';

interface LoginScreenProps {
  onSuccess: () => void;
}

export function LoginScreen({ onSuccess }: LoginScreenProps) {
  const [secret, setSecret] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!secret || busy) {
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await api.login(secret);
      onSuccess();
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 429
          ? '尝试次数过多，请稍后再试'
          : '密码或 API Key 不正确',
      );
      setBusy(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-[var(--bg)] px-6 text-[var(--text)]">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-2">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-[var(--accent)] text-xl font-semibold text-white shadow-sm">
            妙
          </div>
          <h1 className="text-lg font-semibold tracking-tight">妙言 Web</h1>
          <p className="text-[13px] text-[var(--text-muted)]">输入密码继续</p>
        </div>

        <form
          onSubmit={submit}
          className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-sm"
        >
          <label className="block text-[12px] font-medium text-[var(--text-muted)]" htmlFor="secret">
            密码或 API Key
          </label>
          <input
            id="secret"
            type="password"
            autoFocus
            autoComplete="current-password"
            value={secret}
            onChange={(event) => setSecret(event.target.value)}
            className="mt-2 w-full rounded-lg border border-[var(--border-strong)] bg-[var(--surface-raised)] px-3 py-2 text-sm outline-none focus:border-[var(--accent)]"
          />
          {error ? <p className="mt-3 text-[12.5px] text-[var(--danger)]">{error}</p> : null}
          <button
            type="submit"
            disabled={!secret || busy}
            className="mt-4 w-full rounded-lg bg-[var(--accent)] px-3 py-2 text-sm font-medium text-white transition-opacity hover:bg-[var(--accent-strong)] disabled:opacity-50"
          >
            {busy ? '正在验证…' : '继续'}
          </button>
        </form>

        <p className="mt-6 text-center text-[11.5px] text-[var(--text-faint)]">
          自托管部署的访问保护 · 笔记保存在你自己的目录里
        </p>
      </div>
    </main>
  );
}
