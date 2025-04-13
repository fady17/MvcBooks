using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MvcBooks.Migrations
{
    /// <inheritdoc />
    public partial class AddEpubSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookUrl",
                table: "Books",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EpubFileName",
                table: "Books",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EpubFilePath",
                table: "Books",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookUrl",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "EpubFileName",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "EpubFilePath",
                table: "Books");
        }
    }
}
