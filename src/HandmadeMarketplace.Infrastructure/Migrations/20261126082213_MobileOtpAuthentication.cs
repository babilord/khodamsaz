using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HandmadeMarketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MobileOtpAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerifiedMobile",
                table: "AspNetUsers",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MobileOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    PhoneKey = table.Column<string>(type: "text", nullable: false),
                    IpKey = table.Column<string>(type: "text", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Closed = table.Column<bool>(type: "boolean", nullable: false),
                    Delivered = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileOtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_VerifiedMobile",
                table: "AspNetUsers",
                column: "VerifiedMobile",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobileOtpChallenges_IpKey_IssuedAt",
                table: "MobileOtpChallenges",
                columns: new[] { "IpKey", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MobileOtpChallenges_IssuedAt",
                table: "MobileOtpChallenges",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MobileOtpChallenges_PhoneKey_IssuedAt",
                table: "MobileOtpChallenges",
                columns: new[] { "PhoneKey", "IssuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobileOtpChallenges");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_VerifiedMobile",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "VerifiedMobile",
                table: "AspNetUsers");
        }
    }
}
