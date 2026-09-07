using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMetricDisplayMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "sensor_metrics",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "unit",
            table: "sensor_metrics",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "display_name",
            table: "sensor_metrics");

        migrationBuilder.DropColumn(
            name: "unit",
            table: "sensor_metrics");
    }
}
