namespace BookstoreApi.Models;

public class UpdateCartRequest
{
    public required string Isbn { get; set; }
    public int Quantity { get; set; }
}