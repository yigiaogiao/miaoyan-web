import type {
  Asset,
  ChangeEvent,
  ConflictInfo,
  Note,
  NoteSummary,
  Project,
  SaveNoteBody,
  TrashEntry,
  TrashRestoreResult,
  VaultInfo,
} from './types';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly body: unknown;

  constructor(status: number, code: string, message: string, body?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.body = body;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(init?.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
      ...init?.headers,
    },
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const data = text ? (JSON.parse(text) as unknown) : null;

  if (!response.ok) {
    const body = data as { error?: string; message?: string } | null;
    throw new ApiError(
      response.status,
      body?.error ?? 'request-failed',
      body?.message ?? `请求失败 (${response.status})`,
      data,
    );
  }

  return data as T;
}

export function isConflictError(error: unknown): error is ApiError & { body: ConflictInfo } {
  return error instanceof ApiError && error.status === 412;
}

function query(params: Record<string, string | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) {
      search.set(key, value);
    }
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

export const api = {
  vault: () => request<VaultInfo>('/api/vault'),

  projects: () => request<Project[]>('/api/projects'),
  createProject: (name: string) =>
    request<Project>('/api/projects', { method: 'POST', body: JSON.stringify({ name }) }),
  renameProject: (id: string, name: string) =>
    request<Project>(`/api/projects/${encodeURIComponent(id)}`, {
      method: 'PATCH',
      body: JSON.stringify({ name }),
    }),
  deleteProject: (id: string) =>
    request<void>(`/api/projects/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  notes: (projectId: string, search: string) =>
    request<NoteSummary[]>(`/api/notes${query({ project: projectId, q: search })}`),
  note: (id: string) => request<Note>(`/api/notes/${encodeURIComponent(id)}`),
  createNote: (projectId: string, title?: string) =>
    request<Note>('/api/notes', {
      method: 'POST',
      body: JSON.stringify({ projectId, title: title ?? null }),
    }),
  saveNote: (id: string, body: SaveNoteBody) =>
    request<Note>(`/api/notes/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  renameNote: (id: string, title: string | null, projectId: string | null) =>
    request<Note>(`/api/notes/${encodeURIComponent(id)}`, {
      method: 'PATCH',
      body: JSON.stringify({ title, projectId }),
    }),
  deleteNote: (id: string) =>
    request<void>(`/api/notes/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  uploadAsset: (noteId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return request<Asset>(`/api/notes/${encodeURIComponent(noteId)}/assets`, {
      method: 'POST',
      body: form,
    });
  },

  trash: () => request<TrashEntry[]>('/api/trash'),
  restoreTrash: (id: string) =>
    request<TrashRestoreResult>(`/api/trash/${encodeURIComponent(id)}/restore`, { method: 'POST' }),
  purgeTrash: (id: string) =>
    request<void>(`/api/trash/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  eventsUrl: () => '/api/events',
};

export function assetUrl(noteId: string, relative: string): string {
  const clean = relative
    .replace(/^\.\//, '')
    .replace(/^\//, '')
    .split('/')
    .map((segment) => encodeURIComponent(segment))
    .join('/');
  return `/api/notes/${encodeURIComponent(noteId)}/assets/${clean}`;
}

export type { ChangeEvent };
