using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PavamanDroneConfigurator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "drones",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    serial_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    board_type = table.Column<int>(type: "integer", nullable: true),
                    board_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    firmware_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    vehicle_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    friendly_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_connected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "calibration_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drone_id = table.Column<int>(type: "integer", nullable: false),
                    calibration_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    result_data = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    firmware_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calibration_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_calibration_records_drones_drone_id",
                        column: x => x.drone_id,
                        principalTable: "drones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parameter_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drone_id = table.Column<int>(type: "integer", nullable: false),
                    parameter_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parameter_value = table.Column<float>(type: "real", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    changed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    change_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parameter_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_parameter_history_drones_drone_id",
                        column: x => x.drone_id,
                        principalTable: "drones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calibration_records_drone_id_calibration_type_started_at",
                table: "calibration_records",
                columns: new[] { "drone_id", "calibration_type", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_drones_serial_number",
                table: "drones",
                column: "serial_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parameter_history_drone_id_parameter_name_changed_at",
                table: "parameter_history",
                columns: new[] { "drone_id", "parameter_name", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calibration_records");

            migrationBuilder.DropTable(
                name: "parameter_history");

            migrationBuilder.DropTable(
                name: "drones");
        }
    }
}
