using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PavamanDroneConfigurator.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The parameter_locks table was already created by CompleteDatabaseSchema migration.
            // This migration uses IF NOT EXISTS to safely add only the missing indexes
            // without failing on databases where CompleteDatabaseSchema was already applied.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_parameter_locks_is_active""
                    ON parameter_locks (is_active);

                CREATE INDEX IF NOT EXISTS ""IX_parameter_locks_user_id_device_id""
                    ON parameter_locks (user_id, device_id);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_parameter_locks_is_active"";
                DROP INDEX IF EXISTS ""IX_parameter_locks_user_id_device_id"";
            ");
        }
    }
}
