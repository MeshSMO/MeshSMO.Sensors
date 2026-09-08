using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshSMO.Sensors.Gateway.Migrations;

/// <inheritdoc />
public partial class AddGatewayIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
            name: "gateway_identity",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                instance_id = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_gateway_identity", x => x.id));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(
            name: "gateway_identity");
}
