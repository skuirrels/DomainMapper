using DomainMapper.Sample;

var draft = new OrderDraft(42, "Ada Lovelace", 123.45m);
var order = OrderMapper.Place(draft);

Console.WriteLine($"Order {order.Id.Value} for {order.CustomerName}: {order.Total:C}");
