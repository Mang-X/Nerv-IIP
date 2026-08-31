using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingProcessedEventInstanceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "scheduling",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "scheduling",
                table: "processed_integration_events");
        }
    }
}
