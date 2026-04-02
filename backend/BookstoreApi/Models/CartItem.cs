namespace BookstoreApi.Models;

public class CartItem
{
    public required BookDto Book { get; set; }
    public int Quantity { get; set; }
}