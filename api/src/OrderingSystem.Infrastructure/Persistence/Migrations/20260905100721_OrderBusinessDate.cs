using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderBusinessDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "Orders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Existing orders predate the column, so their business date has to be recovered from
            // PlacedAt, which is UTC. Beirut is two or three hours ahead depending on the season,
            // so a plain CAST would file a late evening's orders under the previous day for half
            // the year — and the check constraint below would reject the 0001-01-01 default
            // outright, which is the point: no order may be left without a real date.
            //
            // The zone name differs by host: SQL Server on Linux carries the IANA database,
            // Windows carries its own names. Rather than guess, ask the server which it has.
            migrationBuilder.Sql(
                """
                DECLARE @zone sysname = (
                    SELECT TOP 1 name FROM sys.time_zone_info
                    WHERE name IN ('Asia/Beirut', 'Middle East Standard Time')
                    ORDER BY CASE name WHEN 'Asia/Beirut' THEN 0 ELSE 1 END);

                IF @zone IS NULL
                    -- No timezone data at all. The UTC date is wrong by at most one day for
                    -- orders placed late in the evening, and is the best available answer.
                    UPDATE [Orders] SET [BusinessDate] = CAST([PlacedAt] AS date);
                ELSE
                    EXEC(N'UPDATE [Orders] SET [BusinessDate] = CAST([PlacedAt] AT TIME ZONE ''' + @zone + N''' AS date)');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_BusinessDate",
                table: "Orders",
                columns: new[] { "RestaurantId", "BusinessDate" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_BusinessDateIsReal",
                table: "Orders",
                sql: "[BusinessDate] >= '2020-01-01'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_BusinessDate",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_BusinessDateIsReal",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "Orders");
        }
    }
}
