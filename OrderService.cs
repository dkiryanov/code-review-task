public sealed class OrderService
{
    private readonly AppDbContext _db;
    private readonly IBillingClient _billing;
    private readonly IMessageBus _bus;
    private readonly ILogger<OrderService> _log;

    public OrderService(AppDbContext db, IBillingClient billing, IMessageBus bus, ILogger<OrderService> log)
    {
        _db = db;
        _billing = billing;
        _bus = bus;
        _log = log;
    }

    public async Task<Guid> CreateAsync(CreateOrder cmd)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Number = $"ORD-{DateTime.Now:yyyyMMddHHmmss}", 
            CustomerId = cmd.CustomerId,
            Total = cmd.Total,
            CreatedAt = DateTime.Now,
            Status = "Pending"
        };

        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        var billed = await _billing.ChargeAsync(order.Id, order.Total); // синхронный вызов к другому микросервису
        if (billed)
        {
            order.Status = "Paid";
            await _db.SaveChangesAsync();

            await _bus.PublishAsync("orders.created", new // асинхронная публикация события в брокер
            {
                orderId = order.Id,
                number = order.Number,
                total = order.Total,
                occurredAt = DateTime.Now
            });
            _log.LogInformation("Order {OrderId} created, billed, published", order.Id);
        }
        else
        {
            _log.LogWarning("Billing declined for {OrderId}", order.Id);
        }
        return order.Id;
    }
}