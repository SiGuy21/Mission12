// Entrypoint for the React app.
// Imports Bootstrap styles and mounts `<App />` into `#root`.
import React from 'react';
import ReactDOM from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import './custom.css';
import 'bootstrap/dist/js/bootstrap.bundle.min.js';
import App from './App';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

