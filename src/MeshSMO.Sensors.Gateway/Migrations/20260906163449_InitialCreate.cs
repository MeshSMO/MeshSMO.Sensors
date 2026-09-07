using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Gateway.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "health_probe",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                checked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_health_probe", x => x.id));

        migrationBuilder.CreateTable(
            name: "telemetry_snapshots",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                captured_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                transport = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                payload_json = table.Column<string>(type: "TEXT", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_telemetry_snapshots", x => x.id));

        migrationBuilder.CreateTable(
            name: "telemetry_readings",
            columns: table => new
            {
                snapshot_id = table.Column<long>(type: "INTEGER", nullable: false),
                metric_key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                numeric_value = table.Column<double>(type: "REAL", nullable: true),
                text_value = table.Column<string>(type: "TEXT", nullable: true)
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

        migrationBuilder.CreateIndex(
            name: "ix_telemetry_snapshots_captured_at",
            table: "telemetry_snapshots",
            columns: new[] { "captured_at", "id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "health_probe");

        migrationBuilder.DropTable(
            name: "telemetry_readings");

        migrationBuilder.DropTable(
            name: "telemetry_snapshots");
    }
}
