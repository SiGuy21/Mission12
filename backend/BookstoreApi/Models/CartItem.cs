// Represents an item in the shopping cart.
// Contains the book details and quantity.
namespace BookstoreApi.Models;

public class CartItem
{
    // The book being purchased.
    public required BookDto Book { get; set; }
    // The number of copies of this book in the cart.
    public int Quantity { get; set; }
}