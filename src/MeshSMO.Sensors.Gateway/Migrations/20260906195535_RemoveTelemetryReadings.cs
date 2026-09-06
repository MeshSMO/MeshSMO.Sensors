using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Gateway.Migrations;

/// <inheritdoc />
public partial class RemoveTelemetryReadings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(
            name: "telemetry_readings");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
            name: "telemetry_readings",
            columns: table => new
            {
                snapshot_id = table.Column<long>(type: "INTEGER", nullable: false),
                metric_key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                numeric_value = table.Column<double>(type: "REAL", nullable: true),
                text_value = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_telemetry_readings", x => new { x.snapshot_id, x.metric_key });
                table.ForeignKey(
                    name: "FK_telemetry_readings_telemetry_snapshots_snapshot_id",
                    column: x => x.snapshot_id,
                    principalTable: "telemetry_snapshots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
}
