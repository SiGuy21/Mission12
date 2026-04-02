// Shared types that match the backend JSON payload (camelCase fields).
export type BookDto = {
  title: string;
  author: string;
  publisher: string;
  isbn: string;
  category: string;
  numberOfPages: number;
  price: number;
};

export type PagedResult<T> = {
  page: number;
  pageSize: number;
  totalCount: number;
  items: T[];
};

export type CartItem = {
  book: BookDto;
  quantity: number;
};

export type Cart = {
  items: Record<string, CartItem>;
};

