// Model for updating cart item quantities.
// Contains the ISBN of the book and the new quantity.
namespace BookstoreApi.Models;

public class UpdateCartRequest
{
    // The ISBN of the book to update in the cart.
    public required string Isbn { get; set; }
    // The new quantity for this book (0 to remove from cart).
    public int Quantity { get; set; }
}