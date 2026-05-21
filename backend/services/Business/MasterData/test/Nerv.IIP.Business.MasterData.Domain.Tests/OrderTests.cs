using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.OrderAggregate;
namespace Nerv.IIP.Business.MasterData.Domain.Tests
{
    public class OrderTests
    {
        [Fact]
        public void OrderPaid_Test()
        {
            Order order = new("test", 1);
            Assert.False(order.Paid);
            order.OrderPaid();
            Assert.True(order.Paid);
        }
    }
}