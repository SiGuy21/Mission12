// Represents a shopping cart containing book items.
// Stored in session state for each user.
using System.Collections.Generic;

namespace BookstoreApi.Models;

public class Cart
{
    // Dictionary of cart items, keyed by ISBN.
    // Each ISBN maps to a CartItem containing quantity and book details.
    public Dictionary<string, CartItem> Items { get; set; } = new();
}