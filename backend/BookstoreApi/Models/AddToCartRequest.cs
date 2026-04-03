// Model for adding items to the shopping cart.
// Contains the ISBN of the book to add.
namespace BookstoreApi.Models;

public class AddToCartRequest
{
    // The ISBN of the book to add to the cart.
    public required string Isbn { get; set; }
}