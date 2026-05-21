using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.OrderAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations
{
    internal class OrderEntityTypeConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.ToTable("order");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).UseGuidVersion7ValueGenerator();
            builder.Property(b => b.Name).HasMaxLength(100);
            builder.Property(b => b.Count);
            builder.Property(b => b.Paid);
        }
    }

}