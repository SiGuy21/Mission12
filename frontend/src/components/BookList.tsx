import React, { useEffect, useMemo, useRef, useState } from 'react';
import type { BookDto, PagedResult } from '../types';
import { fetchBookCategories, fetchBooks } from '../api/booksApi';

type SortDir = 'asc' | 'desc';

type CartItem = { book: BookDto; quantity: number };
type CartState = { items: Record<string, CartItem> };

type BrowseState = {
  page: number;
  pageSize: number;
  sortDir: SortDir;
  category: string; // '' means "All"
};

const CART_KEY = 'mission11_cart_v1';
const RETURN_BROWSE_KEY = 'mission11_cart_return_browse_v1';
const pageSizeOptions = [5, 10, 15, 20];

function safeJsonParse<T>(raw: string | null): T | null {
  if (!raw) return null;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

function loadCart(): CartState {
  if (typeof sessionStorage === 'undefined') return { items: {} };

  const parsed = safeJsonParse<CartState>(sessionStorage.getItem(CART_KEY));
  if (!parsed || typeof parsed !== 'object' || !parsed.items) return { items: {} };

  const items: Record<string, CartItem> = {};
  for (const [isbn, item] of Object.entries(parsed.items)) {
    if (!item || typeof item !== 'object') continue;
    const quantity = (item as CartItem).quantity;
    const book = (item as CartItem).book;

    if (typeof quantity !== 'number' || !Number.isFinite(quantity) || quantity < 1) continue;
    if (!book || typeof book !== 'object') continue;
    if (typeof (book as BookDto).isbn !== 'string') continue;

    items[isbn] = { book: book as BookDto, quantity: Math.floor(quantity) };
  }

  return { items };
}

function computeCartTotals(cart: CartState) {
  const items = Object.values(cart.items);
  const totalItems = items.reduce((sum, it) => sum + it.quantity, 0);
  const total = items.reduce((sum, it) => sum + it.quantity * it.book.price, 0);
  return { items, totalItems, total };
}

// Displays a paginated catalog of books with category filtering and a cart.
export default function BookList() {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(5);
  const [sortDir, setSortDir] = useState<SortDir>('asc');
  const [category, setCategory] = useState<string>(''); // '' means "All"

  const [categories, setCategories] = useState<string[]>([]);
  const [categoriesLoading, setCategoriesLoading] = useState(false);

  const [data, setData] = useState<PagedResult<BookDto> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [cart, setCart] = useState<CartState>(() => loadCart());
  const cartOffcanvasRef = useRef<HTMLDivElement | null>(null);

  const cartTotals = useMemo(() => computeCartTotals(cart), [cart]);

  useEffect(() => {
    if (typeof sessionStorage === 'undefined') return;
    sessionStorage.setItem(CART_KEY, JSON.stringify(cart));
  }, [cart]);

  useEffect(() => {
    const controller = new AbortController();
    setCategoriesLoading(true);
    fetchBookCategories({ signal: controller.signal })
      .then((cats) => setCategories(cats))
      .catch((e) => {
        if (e instanceof DOMException && e.name === 'AbortError') return;
        // If categories fail, we can still browse with "All Categories".
      })
      .finally(() => setCategoriesLoading(false));

    return () => controller.abort();
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);

    fetchBooks({ page, pageSize, sort: 'title', sortDir, category: category || undefined, signal: controller.signal })
      .then(setData)
      .catch((e) => {
        if (e instanceof DOMException && e.name === 'AbortError') return;
        setError(e instanceof Error ? e.message : String(e));
      })
      .finally(() => setLoading(false));

    return () => controller.abort();
  }, [page, pageSize, sortDir, category]);

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  const visiblePages = useMemo(() => {
    const pages: number[] = [];
    const windowSize = 5;
    const half = Math.floor(windowSize / 2);

    const start = Math.max(1, page - half);
    const end = Math.min(totalPages, start + windowSize - 1);

    for (let p = start; p <= end; p++) pages.push(p);
    return pages;
  }, [page, totalPages]);

  const toggleSortDir = () => {
    setPage(1);
    setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
  };

  const openCartOffcanvas = () => {
    if (typeof sessionStorage !== 'undefined') {
      const browse: BrowseState = { page, pageSize, sortDir, category };
      sessionStorage.setItem(RETURN_BROWSE_KEY, JSON.stringify(browse));
    }

    const el = cartOffcanvasRef.current;
    if (!el) return;

    const bs = (window as any).bootstrap;
    const Offcanvas = bs?.Offcanvas;
    if (!Offcanvas) return;

    const instance = Offcanvas.getOrCreateInstance(el);
    instance.show();
  };

  const continueShopping = () => {
    const parsed = safeJsonParse<BrowseState>(sessionStorage.getItem(RETURN_BROWSE_KEY));
    if (!parsed) return;

    setPage(Number.isFinite(parsed.page) && parsed.page >= 1 ? parsed.page : 1);
    setPageSize(Number.isFinite(parsed.pageSize) && parsed.pageSize >= 1 ? parsed.pageSize : 5);
    setSortDir(parsed.sortDir === 'desc' ? 'desc' : 'asc');
    setCategory(typeof parsed.category === 'string' ? parsed.category : '');
  };

  const addToCart = (book: BookDto) => {
    setCart((prev) => {
      const existing = prev.items[book.isbn];
      const nextQty = (existing?.quantity ?? 0) + 1;
      return {
        items: {
          ...prev.items,
          [book.isbn]: {
            book,
            quantity: nextQty,
          },
        },
      };
    });

    openCartOffcanvas();
  };

  const adjustQuantity = (isbn: string, delta: number) => {
    setCart((prev) => {
      const item = prev.items[isbn];
      if (!item) return prev;

      const nextQty = item.quantity + delta;
      if (nextQty <= 0) {
        const { [isbn]: _removed, ...rest } = prev.items;
        return { items: rest };
      }

      return {
        items: {
          ...prev.items,
          [isbn]: {
            ...item,
            quantity: nextQty,
          },
        },
      };
    });
  };

  return (
    <div className="container mt-4">
      <div className="row g-3 align-items-start">
        <div className="col-lg-9">
          <div className="d-flex flex-wrap justify-content-between align-items-end gap-2 mb-3">
            <div>
              <h1 className="h4 mb-1">Online Bookstore</h1>
              <div className="form-text">Filter by category, paginate, and add books to your shopping cart.</div>
            </div>

            <div className="d-flex align-items-center gap-2">
              <button
                className="btn btn-outline-secondary btn-sm d-lg-none"
                type="button"
                data-bs-toggle="collapse"
                data-bs-target="#filterCollapse"
                aria-expanded="false"
                aria-controls="filterCollapse"
              >
                Filters
              </button>

              <button
                className="btn btn-primary btn-sm d-lg-none"
                type="button"
                onClick={openCartOffcanvas}
                disabled={cartTotals.totalItems <= 0}
              >
                Cart ({cartTotals.totalItems})
              </button>
            </div>
          </div>

          {error && (
            <div className="alert alert-danger" role="alert">
              {error}
            </div>
          )}

          {loading && <div className="text-muted">Loading...</div>}

          {!loading && data && (
            <>
              <div className="collapse show mb-3" id="filterCollapse">
                <div className="card border-0 bg-light">
                  <div className="card-body">
                    <div className="row g-3 align-items-end">
                      <div className="col-12 col-md-4">
                        <label className="form-label mb-1" htmlFor="categorySelect">
                          Category
                        </label>
                        <select
                          id="categorySelect"
                          className="form-select"
                          value={category}
                          onChange={(e) => {
                            setPage(1);
                            setCategory(e.target.value);
                          }}
                          disabled={categoriesLoading}
                        >
                          <option value="">All Categories</option>
                          {categories.map((c) => (
                            <option key={c} value={c}>
                              {c}
                            </option>
                          ))}
                        </select>
                      </div>

                      <div className="col-6 col-md-3">
                        <label className="form-label mb-1" htmlFor="pageSizeSelect">
                          Results per page
                        </label>
                        <select
                          id="pageSizeSelect"
                          className="form-select"
                          value={pageSize}
                          onChange={(e) => {
                            setPage(1);
                            setPageSize(Number(e.target.value));
                          }}
                        >
                          {pageSizeOptions.map((n) => (
                            <option key={n} value={n}>
                              {n}
                            </option>
                          ))}
                        </select>
                      </div>

                      <div className="col-6 col-md-3">
                        <label className="form-label mb-1">Sorting</label>
                        <button className="btn btn-outline-primary w-100" type="button" onClick={toggleSortDir}>
                          Title: {sortDir === 'asc' ? 'A-Z' : 'Z-A'}
                        </button>
                      </div>

                      <div className="col-12 col-md-2">
                        <div className="text-muted small">
                          Page {page} of {totalPages}
                        </div>
                        <div className="small text-muted">{data.totalCount} books</div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <div className="row g-3">
                {data.items.map((b) => (
                  <div className="col-12 col-md-6 col-lg-4" key={b.isbn}>
                    <div className="card h-100 shadow-sm">
                      <div className="card-body d-flex flex-column">
                        <div className="d-flex justify-content-between align-items-start gap-2">
                          <div>
                            <div className="fw-semibold mb-1">{b.title}</div>
                            <div className="text-muted small">
                              {b.author} • {b.publisher}
                            </div>
                          </div>
                          <span className="badge text-bg-secondary">{b.category}</span>
                        </div>

                        <div className="mt-3 small text-muted">
                          <div>
                            <strong>ISBN:</strong> {b.isbn}
                          </div>
                          <div>
                            <strong>Pages:</strong> {b.numberOfPages}
                          </div>
                        </div>

                        <div className="mt-auto pt-3">
                          <div className="d-flex justify-content-between align-items-center">
                            <div>
                              <div className="text-muted small">Price</div>
                              <div className="fw-semibold">${b.price.toFixed(2)}</div>
                            </div>

                            <button className="btn btn-success btn-sm" type="button" onClick={() => addToCart(b)}>
                              Add to Cart
                            </button>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                ))}
              </div>

              <div className="d-flex flex-wrap justify-content-between align-items-center gap-3 mt-4">
                <div className="text-muted">
                  Page {page} of {totalPages} (Total books: {data.totalCount})
                </div>

                <nav aria-label="Book list pagination">
                  <ul className="pagination mb-0">
                    <li className={`page-item ${page <= 1 ? 'disabled' : ''}`}>
                      <button className="page-link" type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                        Prev
                      </button>
                    </li>

                    {visiblePages.map((p) => (
                      <li key={p} className={`page-item ${p === page ? 'active' : ''}`}>
                        <button className="page-link" type="button" onClick={() => setPage(p)}>
                          {p}
                        </button>
                      </li>
                    ))}

                    <li className={`page-item ${page >= totalPages ? 'disabled' : ''}`}>
                      <button
                        className="page-link"
                        type="button"
                        disabled={page >= totalPages}
                        onClick={() => setPage((p) => p + 1)}
                      >
                        Next
                      </button>
                    </li>
                  </ul>
                </nav>
              </div>
            </>
          )}
        </div>

        <div className="col-lg-3">
          <div className="card">
            <div className="card-body">
              <div className="d-flex justify-content-between align-items-start gap-2 mb-2">
                <div>
                  <div className="fw-semibold">Cart Summary</div>
                  <div className="text-muted small">Session-persistent cart</div>
                </div>
                <div className="text-end">
                  <div className="fw-semibold">{cartTotals.totalItems} items</div>
                  <div className="text-muted small">${cartTotals.total.toFixed(2)}</div>
                </div>
              </div>

              {cartTotals.totalItems <= 0 ? (
                <div className="text-muted">Your cart is empty.</div>
              ) : (
                <>
                  <div className="small text-muted mb-1">Items</div>
                  <div className="list-group list-group-flush">
                    {cartTotals.items.slice(0, 3).map((it) => (
                      <div
                        key={it.book.isbn}
                        className="list-group-item px-0 py-2 d-flex justify-content-between align-items-start gap-2"
                      >
                        <div className="me-2">
                          <div className="small fw-semibold">{it.book.title}</div>
                          <div className="text-muted small">Qty {it.quantity}</div>
                        </div>
                        <div className="fw-semibold">${(it.quantity * it.book.price).toFixed(2)}</div>
                      </div>
                    ))}
                  </div>
                  {cartTotals.items.length > 3 && (
                    <div className="small text-muted mt-2">+{cartTotals.items.length - 3} more</div>
                  )}

                  <button className="btn btn-primary w-100 mt-3" type="button" onClick={openCartOffcanvas}>
                    View Cart
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Bootstrap attributes used (for TAs):
          - Collapse filter: data-bs-toggle="collapse", data-bs-target="#filterCollapse" (see the "Filters" button).
          - Offcanvas cart: data-bs-dismiss="offcanvas" (see "Continue Shopping" + close button).
       */}
      <div
        className="offcanvas offcanvas-end"
        tabIndex={-1}
        id="cartOffcanvas"
        aria-labelledby="cartOffcanvasLabel"
        ref={cartOffcanvasRef}
      >
        <div className="offcanvas-header">
          <h5 className="offcanvas-title" id="cartOffcanvasLabel">
            Your Cart
          </h5>
          <button type="button" className="btn-close" data-bs-dismiss="offcanvas" aria-label="Close" />
        </div>

        <div className="offcanvas-body">
          {cartTotals.totalItems <= 0 ? (
            <div className="text-muted">Your cart is empty.</div>
          ) : (
            <>
              <div className="list-group mb-3">
                {cartTotals.items.map((it) => {
                  const subtotal = it.quantity * it.book.price;
                  return (
                    <div key={it.book.isbn} className="list-group-item">
                      <div className="d-flex justify-content-between align-items-start gap-2">
                        <div className="me-2">
                          <div className="fw-semibold">{it.book.title}</div>
                          <div className="text-muted small">
                            {it.book.author} • {it.book.category}
                          </div>
                        </div>
                        <div className="text-end">
                          <div className="text-muted small">Unit</div>
                          <div className="fw-semibold">${it.book.price.toFixed(2)}</div>
                        </div>
                      </div>

                      <div className="row g-2 mt-2 align-items-center">
                        <div className="col-auto">
                          <div className="input-group input-group-sm">
                            <button
                              className="btn btn-outline-secondary"
                              type="button"
                              onClick={() => adjustQuantity(it.book.isbn, -1)}
                              aria-label={`Decrease quantity for ${it.book.title}`}
                            >
                              -
                            </button>
                            <input className="form-control" value={it.quantity} readOnly aria-label="Quantity" />
                            <button
                              className="btn btn-outline-secondary"
                              type="button"
                              onClick={() => adjustQuantity(it.book.isbn, +1)}
                              aria-label={`Increase quantity for ${it.book.title}`}
                            >
                              +
                            </button>
                          </div>
                        </div>

                        <div className="col text-end">
                          <div className="text-muted small">Subtotal</div>
                          <div className="fw-semibold">${subtotal.toFixed(2)}</div>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>

              <div className="d-flex justify-content-between align-items-center mb-3">
                <div className="fw-semibold">Total</div>
                <div className="h5 mb-0">${cartTotals.total.toFixed(2)}</div>
              </div>
            </>
          )}

          <div className="d-grid gap-2">
            <button
              type="button"
              className="btn btn-primary"
              data-bs-dismiss="offcanvas"
              onClick={continueShopping}
            >
              Continue Shopping
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

