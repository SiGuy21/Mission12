import type { BookDto, PagedResult } from '../types';

// Base URL for the API. Empty uses same origin (Vite dev proxy or SWA + configured backend).
function apiUrl(pathAndQuery: string): string {
  const env = import.meta.env.VITE_API_BASE_URL?.trim() || "https://bookstoreapp-silas-backend-cffkbxgubjgnb7gp.centralus-01.azurewebsites.net/";
  if (env) {
    const base = env.replace(/\/$/, '');
    const p = pathAndQuery.startsWith('/') ? pathAndQuery : `/${pathAndQuery}`;
    return `${base}${p}`;
  }
  return pathAndQuery.startsWith('/') ? pathAndQuery : `/${pathAndQuery}`;
}

export async function fetchBooks(params: {
  page: number;
  pageSize: number;
  sort: 'title';
  sortDir: 'asc' | 'desc';
  category?: string;
  signal?: AbortSignal;
}): Promise<PagedResult<BookDto>> {
  const url = new URL('/api/books', 'http://placeholder');
  url.searchParams.set('page', String(params.page));
  url.searchParams.set('pageSize', String(params.pageSize));
  url.searchParams.set('sort', params.sort);
  url.searchParams.set('sortDir', params.sortDir);
  if (params.category && params.category.trim().length > 0) {
    url.searchParams.set('category', params.category.trim());
  }

  const res = await fetch(apiUrl(`${url.pathname}${url.search}`), { method: 'GET', signal: params.signal });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `Request failed (${res.status})`);
  }

  return (await res.json()) as PagedResult<BookDto>;
}

export async function fetchBookCategories(params?: { signal?: AbortSignal }): Promise<string[]> {
  const res = await fetch(apiUrl('/api/books/categories'), { method: 'GET', signal: params?.signal });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `Request failed (${res.status})`);
  }

  return (await res.json()) as string[];
}

export async function fetchAllBooks(params?: { signal?: AbortSignal }): Promise<BookDto[]> {
  const all: BookDto[] = [];
  let page = 1;
  const pageSize = 100;

  while (true) {
    const batch = await fetchBooks({
      page,
      pageSize,
      sort: 'title',
      sortDir: 'asc',
      signal: params?.signal,
    });
    all.push(...batch.items);
    if (all.length >= batch.totalCount || batch.items.length === 0) break;
    page += 1;
  }

  return all;
}

export async function createBook(book: BookDto, params?: { signal?: AbortSignal }): Promise<void> {
  const res = await fetch(apiUrl('/api/books'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(book),
    signal: params?.signal,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `Request failed (${res.status})`);
  }
}

export async function updateBook(
  originalIsbn: string,
  book: BookDto,
  params?: { signal?: AbortSignal }
): Promise<void> {
  const path = `/api/books/${encodeURIComponent(originalIsbn)}`;
  const res = await fetch(apiUrl(path), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(book),
    signal: params?.signal,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `Request failed (${res.status})`);
  }
}

export async function deleteBook(isbn: string, params?: { signal?: AbortSignal }): Promise<void> {
  const path = `/api/books/${encodeURIComponent(isbn)}`;
  const res = await fetch(apiUrl(path), { method: 'DELETE', signal: params?.signal });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `Request failed (${res.status})`);
  }
}
