using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sensors",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                mesh_public_key = table.Column<string>(type: "text", nullable: false),
                protocol_id = table.Column<string>(type: "text", nullable: false),
                poll_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                poll_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                poll_max_attempts = table.Column<int>(type: "integer", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                public_visible = table.Column<bool>(type: "boolean", nullable: false),
                public_indexable = table.Column<bool>(type: "boolean", nullable: false),
                latitude = table.Column<double>(type: "double precision", nullable: true),
                longitude = table.Column<double>(type: "double precision", nullable: true),
                location_precision = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_sensors", x => x.id));

        migrationBuilder.CreateTable(
            name: "measurement_samples",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sensor_id = table.Column<Guid>(type: "uuid", nullable: false),
                request_id = table.Column<long>(type: "bigint", nullable: true),
                measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                rssi = table.Column<float>(type: "real", nullable: true),
                snr = table.Column<float>(type: "real", nullable: true),
                round_trip_ms = table.Column<int>(type: "integer", nullable: true),
                protocol_id = table.Column<string>(type: "text", nullable: false),
                raw_payload = table.Column<byte[]>(type: "bytea", nullable: true),
                extra = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_measurement_samples", x => x.id);
                table.ForeignKey(
                    name: "FK_measurement_samples_sensors_sensor_id",
                    column: x => x.sensor_id,
                    principalTable: "sensors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "poll_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sensor_id = table.Column<Guid>(type: "uuid", nullable: false),
                request_id = table.Column<long>(type: "bigint", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempt_number = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                error_code = table.Column<string>(type: "text", nullable: true),
                error_message = table.Column<string>(type: "text", nullable: true),
                round_trip_ms = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_poll_attempts", x => x.id);
                table.ForeignKey(
                    name: "FK_poll_attempts_sensors_sensor_id",
                    column: x => x.sensor_id,
                    principalTable: "sensors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sensor_metrics",
            columns: table => new
            {
                sensor_id = table.Column<Guid>(type: "uuid", nullable: false),
                metric_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sensor_metrics", x => new { x.sensor_id, x.metric_key });
                table.ForeignKey(
                    name: "FK_sensor_metrics_sensors_sensor_id",
                    column: x => x.sensor_id,
                    principalTable: "sensors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sensor_status",
            columns: table => new
            {
                sensor_id = table.Column<Guid>(type: "uuid", nullable: false),
                state = table.Column<string>(type: "text", nullable: false),
                last_poll_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                last_rssi = table.Column<float>(type: "real", nullable: true),
                last_snr = table.Column<float>(type: "real", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sensor_status", x => x.sensor_id);
                table.ForeignKey(
                    name: "FK_sensor_status_sensors_sensor_id",
                    column: x => x.sensor_id,
                    principalTable: "sensors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "measurement_values",
            columns: table => new
            {
                sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                metric_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                sensor_id = table.Column<Guid>(type: "uuid", nullable: false),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                numeric_value = table.Column<double>(type: "double precision", nullable: true),
                text_value = table.Column<string>(type: "text", nullable: true),
                unit = table.Column<string>(type: "text", nullable: true),
                quality = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_measurement_values", x => new { x.sample_id, x.metric_key });
                table.ForeignKey(
                    name: "FK_measurement_values_measurement_samples_sample_id",
                    column: x => x.sample_id,
                    principalTable: "measurement_samples",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_measurement_values_sensors_sensor_id",
                    column: x => x.sensor_id,
                    principalTable: "sensors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_measurement_samples_sensor_received",
            table: "measurement_samples",
            columns: new[] { "sensor_id", "received_at" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ux_measurement_samples_sensor_request",
            table: "measurement_samples",
            columns: new[] { "sensor_id", "request_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_measurement_values_sensor_metric_timestamp",
            table: "measurement_values",
            columns: new[] { "sensor_id", "metric_key", "timestamp" },
            descending: new[] { false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_poll_attempts_sensor_started",
            table: "poll_attempts",
            columns: new[] { "sensor_id", "started_at" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ux_sensors_mesh_public_key",
            table: "sensors",
            column: "mesh_public_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_sensors_slug",
            table: "sensors",
            column: "slug",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "measurement_values");

        migrationBuilder.DropTable(
            name: "poll_attempts");

        migrationBuilder.DropTable(
            name: "sensor_metrics");

        migrationBuilder.DropTable(
            name: "sensor_status");

        migrationBuilder.DropTable(
            name: "measurement_samples");

        migrationBuilder.DropTable(
            name: "sensors");
    }
}
