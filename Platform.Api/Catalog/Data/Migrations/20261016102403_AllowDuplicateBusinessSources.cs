using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Api.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowDuplicateBusinessSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_businesses_source_platform_external_id",
                table: "businesses");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_source_platform_external_id",
                table: "businesses",
                columns: new[] { "source_platform", "external_id" },
                filter: "external_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_businesses_source_platform_external_id",
                table: "businesses");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_source_platform_external_id",
                table: "businesses",
                columns: new[] { "source_platform", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");
        }
    }
}
