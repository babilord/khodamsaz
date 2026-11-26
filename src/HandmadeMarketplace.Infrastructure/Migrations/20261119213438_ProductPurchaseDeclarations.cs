using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HandmadeMarketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductPurchaseDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseDeclarations",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseDeclarations", x => new { x.ProductId, x.UserId, x.RequestId });
                    table.CheckConstraint("CK_PurchaseDeclaration_Number", "\"Number\" BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_PurchaseDeclarations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseDeclarations_Listings_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDeclarations_ProductId_UserId_Number",
                table: "PurchaseDeclarations",
                columns: new[] { "ProductId", "UserId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDeclarations_UserId",
                table: "PurchaseDeclarations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseDeclarations");
        }
    }
}
