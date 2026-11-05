import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import { useI18n } from '../lib/i18n';
import { useUiStore } from '../store/ui';

interface LoginScreenProps {
  onSuccess: () => void;
}

export function LoginScreen({ onSuccess }: LoginScreenProps) {
  const { language, t } = useI18n();
  const setLanguage = useUiStore((state) => state.setLanguage);
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
        err instanceof ApiError && err.status === 429 ? t('login.rateLimited') : t('login.wrong'),
      );
      setBusy(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-[var(--bg)] px-6 text-[var(--text)]">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-2">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-[var(--accent)] text-xl font-semibold text-white shadow-sm">
            {language === 'zh' ? '妙' : 'M'}
          </div>
          <h1 className="text-lg font-semibold tracking-tight">{t('brand')}</h1>
          <p className="text-[13px] text-[var(--text-muted)]">{t('login.subtitle')}</p>
        </div>

        <form
          onSubmit={submit}
          className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-sm"
        >
          <label className="block text-[12px] font-medium text-[var(--text-muted)]" htmlFor="secret">
            {t('login.secretLabel')}
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
            {busy ? t('login.busy') : t('login.continue')}
          </button>
        </form>

        <div className="mt-5 flex items-center justify-center gap-2 text-[12px] text-[var(--text-muted)]">
          <button
            type="button"
            onClick={() => setLanguage('zh')}
            className={language === 'zh' ? 'font-semibold text-[var(--accent)]' : 'hover:text-[var(--text)]'}
          >
            中文
          </button>
          <span className="text-[var(--text-faint)]">/</span>
          <button
            type="button"
            onClick={() => setLanguage('en')}
            className={language === 'en' ? 'font-semibold text-[var(--accent)]' : 'hover:text-[var(--text)]'}
          >
            English
          </button>
        </div>

        <p className="mt-4 text-center text-[11.5px] text-[var(--text-faint)]">{t('login.footer')}</p>
      </div>
    </main>
  );
}
