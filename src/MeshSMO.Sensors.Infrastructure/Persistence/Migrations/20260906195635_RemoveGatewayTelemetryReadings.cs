using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveGatewayTelemetryReadings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(
            name: "gateway_telemetry_readings");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gateway_telemetry_readings",
            columns: table => new
            {
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                metric_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                numeric_value = table.Column<double>(type: "double precision", nullable: true),
                text_value = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gateway_telemetry_readings", x => new { x.snapshot_id, x.metric_key });
                table.ForeignKey(
                    name: "FK_gateway_telemetry_readings_gateway_telemetry_snapshots_snap~",
                    column: x => x.snapshot_id,
                    principalTable: "gateway_telemetry_snapshots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gateway_telemetry_readings_metric_key",
            table: "gateway_telemetry_readings",
            column: "metric_key");
    }
}
