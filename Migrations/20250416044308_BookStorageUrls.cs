using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MvcBooks.Migrations
{
    /// <inheritdoc />
    public partial class BookStorageUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpubFileName",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "EpubFilePath",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "PdfFileName",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "PdfFilePath",
                table: "Books");

            migrationBuilder.AlterColumn<string>(
                name: "CoverImageUrl",
                table: "Books",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookSourceType",
                table: "Books",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookSourceType",
                table: "Books");

            migrationBuilder.AlterColumn<string>(
                name: "CoverImageUrl",
                table: "Books",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

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

            migrationBuilder.AddColumn<string>(
                name: "PdfFileName",
                table: "Books",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfFilePath",
                table: "Books",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }
    }
}
