using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Api.Admin.Migrations
{
    /// <inheritdoc />
    public partial class RenameAdminEmailToUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename instead of drop/add (what EF generates by default) so existing admins keep their logins
            migrationBuilder.RenameColumn(
                name: "email",
                table: "admin_users",
                newName: "username");

            migrationBuilder.RenameIndex(
                name: "ix_admin_users_email",
                table: "admin_users",
                newName: "ix_admin_users_username");

            migrationBuilder.AlterColumn<string>(
                name: "username",
                table: "admin_users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "username",
                table: "admin_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.RenameIndex(
                name: "ix_admin_users_username",
                table: "admin_users",
                newName: "ix_admin_users_email");

            migrationBuilder.RenameColumn(
                name: "username",
                table: "admin_users",
                newName: "email");
        }
    }
}
