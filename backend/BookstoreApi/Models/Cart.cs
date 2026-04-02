using System.Collections.Generic;

namespace BookstoreApi.Models;

public class Cart
{
    public Dictionary<string, CartItem> Items { get; set; } = new();
}