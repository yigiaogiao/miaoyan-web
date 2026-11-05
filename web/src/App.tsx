import { useEffect } from 'react';
import { Navigate, Route, Routes } from 'react-router';
import { Workspace } from './components/Workspace';
import { useUiStore } from './store/ui';

export default function App() {
  const theme = useUiStore((state) => state.theme);

  useEffect(() => {
    const root = document.documentElement;
    if (theme === 'system') {
      delete root.dataset.theme;
    } else {
      root.dataset.theme = theme;
    }
  }, [theme]);

  return (
    <Routes>
      <Route path="/" element={<Workspace />} />
      <Route path="/note/:noteId" element={<Workspace />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
