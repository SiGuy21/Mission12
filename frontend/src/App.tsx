// Root React component: catalog + admin routes.
import React from 'react';
import { BrowserRouter, Link, Route, Routes } from 'react-router-dom';
import AdminBooks from './components/AdminBooks';
import BookList from './components/BookList';

export default function App() {
  return (
    <BrowserRouter>
      <nav className="border-bottom bg-light">
        <div className="container py-2 d-flex flex-wrap gap-3 align-items-center">
          <span className="fw-semibold text-muted small">Bookstore</span>
          <Link className="small" to="/">
            Shop
          </Link>
          <Link className="small" to="/adminbooks">
            Admin books
          </Link>
        </div>
      </nav>
      <Routes>
        <Route path="/" element={<BookList />} />
        <Route path="/adminbooks" element={<AdminBooks />} />
      </Routes>
    </BrowserRouter>
  );
}
