using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Platform.Api.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferingSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Generated columns accept only IMMUTABLE functions, and array_to_string is STABLE
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION catalog_array_to_text(text[]) RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE
                AS $$ SELECT coalesce(array_to_string($1, ' '), '') $$;
                """);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "offerings",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('english'::regconfig, coalesce(name, '')), 'A') ||\nsetweight(to_tsvector('english'::regconfig, catalog_array_to_text(aliases)), 'A') ||\nsetweight(to_tsvector('english'::regconfig, catalog_array_to_text(tags)), 'B') ||\nsetweight(to_tsvector('english'::regconfig, coalesce(category, '')), 'B') ||\nsetweight(to_tsvector('english'::regconfig, coalesce(short_description, '')), 'C') ||\nsetweight(to_tsvector('english'::regconfig, coalesce(audience_level_text, '') || ' ' || coalesce(audience_prerequisites, '')), 'C') ||\nsetweight(to_tsvector('english'::regconfig, coalesce(description, '')), 'D') ||\nsetweight(to_tsvector('english'::regconfig, catalog_array_to_text(inclusions)), 'D')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_offerings_search_vector",
                table: "offerings",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offerings_search_vector",
                table: "offerings");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "offerings");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS catalog_array_to_text(text[]);");
        }
    }
}
