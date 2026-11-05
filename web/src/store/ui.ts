import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { ThemeMode, ViewMode } from '../api/types';

interface UiState {
  selectedProjectId: string;
  viewMode: ViewMode;
  theme: ThemeMode;
  sidebarOpen: boolean;
  listOpen: boolean;
  setProject: (id: string) => void;
  setViewMode: (mode: ViewMode) => void;
  setTheme: (theme: ThemeMode) => void;
  toggleSidebar: () => void;
  toggleList: () => void;
  showBoth: () => void;
  setPanels: (sidebarOpen: boolean, listOpen: boolean) => void;
}

export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      selectedProjectId: 'all',
      viewMode: 'split',
      theme: 'system',
      sidebarOpen: true,
      listOpen: true,
      setProject: (id) => set({ selectedProjectId: id }),
      setViewMode: (viewMode) => set({ viewMode }),
      setTheme: (theme) => set({ theme }),
      toggleSidebar: () => set((state) => ({ sidebarOpen: !state.sidebarOpen })),
      toggleList: () => set((state) => ({ listOpen: !state.listOpen })),
      showBoth: () => set({ sidebarOpen: true, listOpen: true }),
      setPanels: (sidebarOpen, listOpen) => set({ sidebarOpen, listOpen }),
    }),
    {
      name: 'miaoyan-ui',
      partialize: (state) => ({
        viewMode: state.viewMode,
        theme: state.theme,
        sidebarOpen: state.sidebarOpen,
        listOpen: state.listOpen,
      }),
    },
  ),
);
