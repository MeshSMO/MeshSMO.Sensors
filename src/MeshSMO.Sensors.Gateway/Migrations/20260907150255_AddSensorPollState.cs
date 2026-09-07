using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorPollState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sensor_poll_states",
                columns: table => new
                {
                    sensor_slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    last_poll_started_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensor_poll_states", x => x.sensor_slug);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sensor_poll_states");
        }
    }
}
