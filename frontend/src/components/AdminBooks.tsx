import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type { BookDto } from '../types';
import { createBook, deleteBook, fetchAllBooks, updateBook } from '../api/booksApi';

const emptyBook = (): BookDto => ({
  title: '',
  author: '',
  publisher: '',
  isbn: '',
  category: '',
  numberOfPages: 1,
  price: 0,
});

export default function AdminBooks() {
  const [books, setBooks] = useState<BookDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [form, setForm] = useState<BookDto>(emptyBook);
  const [editingOriginalIsbn, setEditingOriginalIsbn] = useState<string | null>(null);

  const load = useCallback(async () => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    try {
      const list = await fetchAllBooks({ signal: controller.signal });
      setBooks(list);
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return;
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const resetForm = () => {
    setForm(emptyBook());
    setEditingOriginalIsbn(null);
  };

  const startEdit = (b: BookDto) => {
    setForm({ ...b });
    setEditingOriginalIsbn(b.isbn);
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editingOriginalIsbn) {
        await updateBook(editingOriginalIsbn, form);
      } else {
        await createBook(form);
      }
      resetForm();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  const onDelete = async (isbn: string) => {
    if (!window.confirm(`Delete book with ISBN ${isbn}?`)) return;
    setSaving(true);
    setError(null);
    try {
      await deleteBook(isbn);
      if (editingOriginalIsbn === isbn) resetForm();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="container mt-4 mb-5">
      <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
        <div>
          <h1 className="h4 mb-1">Admin — Books</h1>
          <div className="form-text">Add, edit, or remove books in the database.</div>
        </div>
        <Link className="btn btn-outline-secondary" to="/">
          Back to store
        </Link>
      </div>

      {error && (
        <div className="alert alert-danger" role="alert">
          {error}
        </div>
      )}

      <div className="card mb-4">
        <div className="card-header">{editingOriginalIsbn ? 'Edit book' : 'Add book'}</div>
        <div className="card-body">
          <form className="row g-3" onSubmit={submit}>
            <div className="col-md-6">
              <label className="form-label" htmlFor="title">
                Title
              </label>
              <input
                id="title"
                className="form-control"
                value={form.title}
                onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))}
                required
              />
            </div>
            <div className="col-md-6">
              <label className="form-label" htmlFor="author">
                Author
              </label>
              <input
                id="author"
                className="form-control"
                value={form.author}
                onChange={(e) => setForm((f) => ({ ...f, author: e.target.value }))}
                required
              />
            </div>
            <div className="col-md-6">
              <label className="form-label" htmlFor="publisher">
                Publisher
              </label>
              <input
                id="publisher"
                className="form-control"
                value={form.publisher}
                onChange={(e) => setForm((f) => ({ ...f, publisher: e.target.value }))}
                required
              />
            </div>
            <div className="col-md-6">
              <label className="form-label" htmlFor="isbn">
                ISBN
              </label>
              <input
                id="isbn"
                className="form-control"
                value={form.isbn}
                onChange={(e) => setForm((f) => ({ ...f, isbn: e.target.value }))}
                required
              />
            </div>
            <div className="col-md-6">
              <label className="form-label" htmlFor="category">
                Category
              </label>
              <input
                id="category"
                className="form-control"
                value={form.category}
                onChange={(e) => setForm((f) => ({ ...f, category: e.target.value }))}
                required
              />
            </div>
            <div className="col-md-3">
              <label className="form-label" htmlFor="pages">
                Pages
              </label>
              <input
                id="pages"
                type="number"
                min={1}
                className="form-control"
                value={form.numberOfPages}
                onChange={(e) => setForm((f) => ({ ...f, numberOfPages: Number(e.target.value) }))}
                required
              />
            </div>
            <div className="col-md-3">
              <label className="form-label" htmlFor="price">
                Price
              </label>
              <input
                id="price"
                type="number"
                min={0}
                step="0.01"
                className="form-control"
                value={form.price}
                onChange={(e) => setForm((f) => ({ ...f, price: Number(e.target.value) }))}
                required
              />
            </div>
            <div className="col-12 d-flex flex-wrap gap-2">
              <button className="btn btn-primary" type="submit" disabled={saving}>
                {editingOriginalIsbn ? 'Save changes' : 'Add book'}
              </button>
              {editingOriginalIsbn && (
                <button className="btn btn-outline-secondary" type="button" onClick={resetForm} disabled={saving}>
                  Cancel edit
                </button>
              )}
            </div>
          </form>
        </div>
      </div>

      <div className="d-flex justify-content-between align-items-center mb-2">
        <h2 className="h6 mb-0">All books ({books.length})</h2>
        <button className="btn btn-sm btn-outline-primary" type="button" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>

      {loading && <div className="text-muted">Loading...</div>}

      {!loading && books.length === 0 && <div className="text-muted">No books loaded.</div>}

      {!loading && books.length > 0 && (
        <div className="table-responsive">
          <table className="table table-sm table-striped align-middle">
            <thead>
              <tr>
                <th>Title</th>
                <th>Author</th>
                <th>ISBN</th>
                <th>Category</th>
                <th className="text-end">Price</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {books.map((b) => (
                <tr key={b.isbn}>
                  <td className="fw-semibold">{b.title}</td>
                  <td>{b.author}</td>
                  <td>
                    <code>{b.isbn}</code>
                  </td>
                  <td>{b.category}</td>
                  <td className="text-end">${b.price.toFixed(2)}</td>
                  <td className="text-end text-nowrap">
                    <button className="btn btn-sm btn-outline-primary me-1" type="button" onClick={() => startEdit(b)} disabled={saving}>
                      Edit
                    </button>
                    <button className="btn btn-sm btn-outline-danger" type="button" onClick={() => void onDelete(b.isbn)} disabled={saving}>
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
