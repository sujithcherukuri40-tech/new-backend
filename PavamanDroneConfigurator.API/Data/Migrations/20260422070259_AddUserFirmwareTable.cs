using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PavamanDroneConfigurator.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFirmwareTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_firmwares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    s3_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    firmware_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    firmware_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    vehicle_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Copter"),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_downloaded = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    download_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_firmwares", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_firmwares_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_firmwares_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_firmwares_s3_key",
                table: "user_firmwares",
                column: "s3_key");

            migrationBuilder.CreateIndex(
                name: "IX_user_firmwares_uploaded_by",
                table: "user_firmwares",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_user_firmwares_user_id",
                table: "user_firmwares",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_firmwares_user_id_is_active",
                table: "user_firmwares",
                columns: new[] { "user_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_user_firmwares_vehicle_type",
                table: "user_firmwares",
                column: "vehicle_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_firmwares");
        }
    }
}
