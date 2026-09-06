using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPollAttemptRequestAttemptIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_poll_attempts_sensor_request_attempt",
                table: "poll_attempts",
                columns: new[] { "sensor_id", "request_id", "attempt_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_poll_attempts_sensor_request_attempt",
                table: "poll_attempts");
        }
    }
}
