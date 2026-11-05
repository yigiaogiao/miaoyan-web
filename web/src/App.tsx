import { useEffect, useState } from 'react';
import { Navigate, Route, Routes } from 'react-router';
import { api } from './api/client';
import { LoginScreen } from './components/LoginScreen';
import { Workspace } from './components/Workspace';
import { useUiStore } from './store/ui';

type AuthState = 'checking' | 'required' | 'ok';

export default function App() {
  const theme = useUiStore((state) => state.theme);
  const [auth, setAuth] = useState<AuthState>('checking');

  useEffect(() => {
    const root = document.documentElement;
    if (theme === 'system') {
      delete root.dataset.theme;
    } else {
      root.dataset.theme = theme;
    }
  }, [theme]);

  useEffect(() => {
    let cancelled = false;
    api
      .authStatus()
      .then((status) => {
        if (!cancelled) {
          setAuth(status.required && !status.authenticated ? 'required' : 'ok');
        }
      })
      .catch(() => {
        if (!cancelled) {
          setAuth('ok');
        }
      });

    const onUnauthorized = () => setAuth('required');
    window.addEventListener('miaoyan:unauthorized', onUnauthorized);
    return () => {
      cancelled = true;
      window.removeEventListener('miaoyan:unauthorized', onUnauthorized);
    };
  }, []);

  if (auth === 'checking') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-[var(--bg)] text-sm text-[var(--text-faint)]">
        妙言 Web
      </div>
    );
  }

  if (auth === 'required') {
    return <LoginScreen onSuccess={() => setAuth('ok')} />;
  }

  return (
    <Routes>
      <Route path="/" element={<Workspace />} />
      <Route path="/note/:noteId" element={<Workspace />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
