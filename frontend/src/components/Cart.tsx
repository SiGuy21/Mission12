import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { Cart, CartItem } from '../types';
import { fetchCart, updateCartItem, removeFromCart } from '../api/booksApi';

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

const RETURN_BROWSE_KEY = 'mission11_cart_return_browse_v1';

type BrowseState = {
  page: number;
  pageSize: number;
  sortDir: 'asc' | 'desc';
  category: string;
};

// Displays the shopping cart with quantity adjustments and continue shopping.
export default function Cart() {
  const navigate = useNavigate();
  const [cart, setCart] = useState<Cart>({ items: {} });
  const [cartLoading, setCartLoading] = useState(false);

  const cartTotals = useMemo(() => computeCartTotals(cart), [cart]);

  useEffect(() => {
    const loadCart = async () => {
      try {
        const fetchedCart = await fetchCart();
        setCart(fetchedCart);
      } catch (e) {
        setCart({ items: {} });
      }
    };
    loadCart();
  }, []);

  const continueShopping = () => {
    const parsed = safeJsonParse<BrowseState>(sessionStorage.getItem(RETURN_BROWSE_KEY));
    if (parsed) {
      navigate(`/?page=${parsed.page}&pageSize=${parsed.pageSize}&sortDir=${parsed.sortDir}&category=${encodeURIComponent(parsed.category)}`);
    } else {
      navigate('/');
    }
  };

  const adjustQuantity = async (isbn: string, delta: number) => {
    const item = cart.items[isbn];
    if (!item) return;

    const nextQty = item.quantity + delta;
    if (nextQty <= 0) {
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
      <h1 className="h3 mb-4">Your Shopping Cart</h1>

      {cartTotals.totalItems <= 0 ? (
        <div className="text-center">
          <div className="text-muted mb-3">Your cart is empty.</div>
          <button className="btn btn-primary" onClick={() => navigate('/')}>
            Continue Shopping
          </button>
        </div>
      ) : (
        <>
          <div className="list-group mb-4 fade-in">
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
                      <div className="text-muted small">Unit Price</div>
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
                          disabled={cartLoading}
                          aria-label={`Decrease quantity for ${it.book.title}`}
                        >
                          -
                        </button>
                        <input className="form-control" value={it.quantity} readOnly aria-label="Quantity" />
                        <button
                          className="btn btn-outline-secondary"
                          type="button"
                          onClick={() => adjustQuantity(it.book.isbn, +1)}
                          disabled={cartLoading}
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

          <div className="d-flex justify-content-between align-items-center mb-4">
            <div className="fw-semibold">Total</div>
            <div className="h4 mb-0">${cartTotals.total.toFixed(2)}</div>
          </div>

          <div className="d-flex gap-2">
            <button className="btn btn-secondary" onClick={continueShopping}>
              Continue Shopping
            </button>
            <button className="btn btn-primary" disabled>
              Checkout (Not Implemented)
            </button>
          </div>
        </>
      )}
    </div>
  );
}