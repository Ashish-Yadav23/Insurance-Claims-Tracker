using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsuranceClaimsTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutAndApprovalFieldsToClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedAmount",
                table: "Claims",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "Claims",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "Claims",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Claims_ApprovedAmount",
                table: "Claims",
                sql: "[ApprovedAmount] IS NULL OR ([ApprovedAmount] > 0 AND [ApprovedAmount] <= [ClaimAmount])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Claims_ApprovedAmount",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ApprovedAmount",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "Claims");
        }
    }
}
