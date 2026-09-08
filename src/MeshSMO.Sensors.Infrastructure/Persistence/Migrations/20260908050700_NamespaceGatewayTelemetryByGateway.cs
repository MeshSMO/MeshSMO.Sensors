using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NamespaceGatewayTelemetryByGateway : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_gateway_telemetry_gateway_snapshot_id",
            table: "gateway_telemetry_snapshots");

        migrationBuilder.AddColumn<Guid>(
            name: "gateway_id",
            table: "gateway_telemetry_snapshots",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.CreateIndex(
            name: "ux_gateway_telemetry_gateway_id_snapshot_id",
            table: "gateway_telemetry_snapshots",
            columns: new[] { "gateway_id", "gateway_snapshot_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_gateway_telemetry_gateway_id_snapshot_id",
            table: "gateway_telemetry_snapshots");

        migrationBuilder.DropColumn(
            name: "gateway_id",
            table: "gateway_telemetry_snapshots");

        migrationBuilder.CreateIndex(
            name: "ux_gateway_telemetry_gateway_snapshot_id",
            table: "gateway_telemetry_snapshots",
            column: "gateway_snapshot_id",
            unique: true);
    }
}
