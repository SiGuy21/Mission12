// AdminBooks page component - provides CRUD operations for books.
// Allows administrators to add, edit, delete, and view all books in the database.
// Includes a form for creating/editing books and a table showing all books.
import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type { BookDto } from '../types';
import { createBook, deleteBook, fetchAllBooks, updateBook } from '../api/booksApi';

// Helper function to create an empty book object for the form.
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
  // State for the list of all books.
  const [books, setBooks] = useState<BookDto[]>([]);
  // State for loading indicator when fetching books.
  const [loading, setLoading] = useState(false);
  // State for error messages.
  const [error, setError] = useState<string | null>(null);
  // State for loading indicator during save operations.
  const [saving, setSaving] = useState(false);

  // State for the book form (add/edit).
  const [form, setForm] = useState<BookDto>(emptyBook);
  // State to track which book is being edited (null for adding new).
  const [editingOriginalIsbn, setEditingOriginalIsbn] = useState<string | null>(null);

  // Function to load all books from the API.
  const load = useCallback(async () => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    try {
      const list = await fetchAllBooks({ signal: controller.signal });
      setBooks(list);
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return;  // Ignore aborted requests
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  // Load books when component mounts.
  useEffect(() => {
    void load();
  }, [load]);

  // Function to reset the form to empty state.
  const resetForm = () => {
    setForm(emptyBook());
    setEditingOriginalIsbn(null);
  };

  // Function to start editing an existing book.
  const startEdit = (b: BookDto) => {
    setForm({ ...b });  // Copy book data to form
    setEditingOriginalIsbn(b.isbn);  // Track which book is being edited
  };

  // Function to submit the form (create or update book).
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editingOriginalIsbn) {
        // Update existing book.
        await updateBook(editingOriginalIsbn, form);
      } else {
        // Create new book.
        await createBook(form);
      }
      resetForm();  // Clear form after successful save
      await load();  // Reload the books list
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  // Function to delete a book after confirmation.
  const onDelete = async (isbn: string) => {
    if (!window.confirm(`Delete book with ISBN ${isbn}?`)) return;  // Confirm deletion
    setSaving(true);
    setError(null);
    try {
      await deleteBook(isbn);
      if (editingOriginalIsbn === isbn) resetForm();  // Clear form if deleting the book being edited
      await load();  // Reload the books list
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="container mt-4 mb-5">
      {/* Page header with title and navigation */}
      <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
        <div>
          <h1 className="h4 mb-1">Admin — Books</h1>
          <div className="form-text">Add, edit, or remove books in the database.</div>
        </div>
        <Link className="btn btn-outline-secondary" to="/">
          Back to store
        </Link>
      </div>

      {/* Error message display */}
      {error && (
        <div className="alert alert-danger" role="alert">
          {error}
        </div>
      )}

      {/* Book form card for adding/editing */}
      <div className="card mb-4">
        <div className="card-header">{editingOriginalIsbn ? 'Edit book' : 'Add book'}</div>
        <div className="card-body">
          <form className="row g-3" onSubmit={submit}>
            {/* Title field */}
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
            {/* Author field */}
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
            {/* Publisher field */}
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
            {/* ISBN field */}
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
            {/* Category field */}
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
            {/* Number of pages field */}
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
            {/* Price field */}
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
            {/* Form action buttons */}
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

      {/* Books list section header */}
      <div className="d-flex justify-content-between align-items-center mb-2">
        <h2 className="h6 mb-0">All books ({books.length})</h2>
        <button className="btn btn-sm btn-outline-primary" type="button" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>

      {/* Loading indicator */}
      {loading && <div className="text-muted">Loading...</div>}

      {/* Empty state */}
      {!loading && books.length === 0 && <div className="text-muted">No books loaded.</div>}

      {/* Books table */}
      {!loading && books.length > 0 && (
        <div className="table-responsive">
          <table className="table table-sm table-striped align-middle fade-in">
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
                    {/* Edit button */}
                    <button className="btn btn-sm btn-outline-primary me-1" type="button" onClick={() => startEdit(b)} disabled={saving}>
                      Edit
                    </button>
                    {/* Delete button */}
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
