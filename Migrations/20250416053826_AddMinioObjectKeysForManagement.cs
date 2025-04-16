using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MvcBooks.Migrations
{
    /// <inheritdoc />
    public partial class AddMinioObjectKeysForManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookFileObjectKey",
                table: "Books",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageObjectKey",
                table: "Books",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookFileObjectKey",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CoverImageObjectKey",
                table: "Books");
        }
    }
}
