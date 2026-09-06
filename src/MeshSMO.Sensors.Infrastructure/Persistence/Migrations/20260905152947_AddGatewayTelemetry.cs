using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddGatewayTelemetry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gateway_telemetry_snapshots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                gateway_snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                transport = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                payload_json = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_gateway_telemetry_snapshots", x => x.id));

        migrationBuilder.CreateTable(
            name: "gateway_telemetry_readings",
            columns: table => new
            {
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                metric_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                numeric_value = table.Column<double>(type: "double precision", nullable: true),
                text_value = table.Column<string>(type: "text", nullable: true)
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

        migrationBuilder.CreateIndex(
            name: "ix_gateway_telemetry_snapshots_captured_at",
            table: "gateway_telemetry_snapshots",
            column: "captured_at");

        migrationBuilder.CreateIndex(
            name: "ux_gateway_telemetry_gateway_snapshot_id",
            table: "gateway_telemetry_snapshots",
            column: "gateway_snapshot_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gateway_telemetry_readings");

        migrationBuilder.DropTable(
            name: "gateway_telemetry_snapshots");
    }
}
