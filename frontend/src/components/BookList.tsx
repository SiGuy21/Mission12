import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { BookDto, PagedResult, Cart, CartItem } from '../types';
import { fetchBookCategories, fetchBooks, fetchCart, addToCart, updateCartItem, removeFromCart } from '../api/booksApi';

type SortDir = 'asc' | 'desc';

type BrowseState = {
  page: number;
  pageSize: number;
  sortDir: SortDir;
  category: string; // '' means "All"
};

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

function computeCartTotals(cart: Cart) {
  const items = Object.values(cart.items);
  const totalItems = items.reduce((sum, it) => sum + it.quantity, 0);
  const total = items.reduce((sum, it) => sum + it.quantity * it.book.price, 0);
  return { items, totalItems, total };
}

// Displays a paginated catalog of books with category filtering and a cart.
export default function BookList() {
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(5);
  const [sortDir, setSortDir] = useState<SortDir>('asc');
  const [category, setCategory] = useState<string>(''); // '' means "All"

  const [categories, setCategories] = useState<string[]>([]);
  const [categoriesLoading, setCategoriesLoading] = useState(false);

  const [data, setData] = useState<PagedResult<BookDto> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [cart, setCart] = useState<Cart>({ items: {} });
  const [cartLoading, setCartLoading] = useState(false);

  const cartTotals = useMemo(() => computeCartTotals(cart), [cart]);

  useEffect(() => {
    const loadCart = async () => {
      try {
        const fetchedCart = await fetchCart();
        setCart(fetchedCart);
      } catch (e) {
        // Cart might not exist, use empty
        setCart({ items: {} });
      }
    };
    loadCart();
  }, []);

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

  const openCart = () => {
    const browse: BrowseState = { page, pageSize, sortDir, category };
    if (typeof sessionStorage !== 'undefined') {
      sessionStorage.setItem(RETURN_BROWSE_KEY, JSON.stringify(browse));
    }
    navigate('/cart');
  };

  const addToCartHandler = async (book: BookDto) => {
    setCartLoading(true);
    try {
      const updatedCart = await addToCart(book.isbn);
      setCart(updatedCart);
      openCart();
    } catch (e) {
      // Handle error, maybe show toast
      console.error('Failed to add to cart', e);
    } finally {
      setCartLoading(false);
    }
  };

  const adjustQuantity = async (isbn: string, delta: number) => {
    const item = cart.items[isbn];
    if (!item) return;

    const nextQty = item.quantity + delta;
    if (nextQty <= 0) {
      // Remove item
      setCartLoading(true);
      try {
        const updatedCart = await removeFromCart(isbn);
        setCart(updatedCart);
      } catch (e) {
        console.error('Failed to remove from cart', e);
      } finally {
        setCartLoading(false);
      }
    } else {
      // Update quantity
      setCartLoading(true);
      try {
        const updatedCart = await updateCartItem(isbn, nextQty);
        setCart(updatedCart);
      } catch (e) {
        console.error('Failed to update cart', e);
      } finally {
        setCartLoading(false);
      }
    }
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
                onClick={openCart}
                disabled={cartTotals.totalItems <= 0 || cartLoading}
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
                    <div className="card h-100 shadow-sm fade-in">
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

                            <button className="btn btn-success btn-sm" type="button" onClick={() => addToCartHandler(b)} disabled={cartLoading}>
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
                <div style={{ cursor: 'pointer' }} onClick={cartTotals.totalItems > 0 ? openCart : undefined}>
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

                  <button className="btn btn-primary w-100 mt-3" type="button" onClick={openCart} disabled={cartLoading}>
                    View Cart
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

