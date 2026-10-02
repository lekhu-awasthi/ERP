using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase64RestaurantOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "KitchenStationId",
                schema: "catalog",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KitchenStations",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PosAreas",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosAreas_BillingLocations_BillingLocationId",
                        column: x => x.BillingLocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosTables",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosAreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    Shape = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosTables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosTables_BillingLocations_BillingLocationId",
                        column: x => x.BillingLocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosTables_PosAreas_PosAreaId",
                        column: x => x.PosAreaId,
                        principalSchema: "pos",
                        principalTable: "PosAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PosOrders",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BillingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PosTableId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Covers = table.Column<int>(type: "int", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoidReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosOrders_BillingLocations_BillingLocationId",
                        column: x => x.BillingLocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosOrders_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosOrders_PosTables_PosTableId",
                        column: x => x.PosTableId,
                        principalSchema: "pos",
                        principalTable: "PosTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KitchenTickets",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SendNumber = table.Column<int>(type: "int", nullable: false),
                    KitchenStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PrintCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitchenTickets_KitchenStations_KitchenStationId",
                        column: x => x.KitchenStationId,
                        principalSchema: "pos",
                        principalTable: "KitchenStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitchenTickets_PosOrders_PosOrderId",
                        column: x => x.PosOrderId,
                        principalSchema: "pos",
                        principalTable: "PosOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PosOrderLines",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConversionFactor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ServiceChargeRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    KitchenStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosOrderLines_KitchenStations_KitchenStationId",
                        column: x => x.KitchenStationId,
                        principalSchema: "pos",
                        principalTable: "KitchenStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosOrderLines_PosOrders_PosOrderId",
                        column: x => x.PosOrderId,
                        principalSchema: "pos",
                        principalTable: "PosOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PosOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosOrderLines_UnitsOfMeasurement_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "catalog",
                        principalTable: "UnitsOfMeasurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KitchenTicketLines",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KitchenTicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenTicketLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitchenTicketLines_KitchenTickets_KitchenTicketId",
                        column: x => x.KitchenTicketId,
                        principalSchema: "pos",
                        principalTable: "KitchenTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KitchenTicketLines_PosOrderLines_PosOrderLineId",
                        column: x => x.PosOrderLineId,
                        principalSchema: "pos",
                        principalTable: "PosOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "LocationId", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001e7"), true, null, "Pos.Order.Operate", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001e8"), true, null, "Pos.Order.Operate", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001e9"), true, null, "Pos.Order.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001ea"), false, null, "Pos.Order.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001eb"), true, null, "Pos.Order.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001ec"), true, null, "Pos.Order.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001ed"), true, null, "Pos.FloorPlan.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001ee"), false, null, "Pos.FloorPlan.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_KitchenStationId",
                schema: "catalog",
                table: "Products",
                column: "KitchenStationId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenStations_OrganizationId_Name",
                schema: "pos",
                table: "KitchenStations",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTicketLines_KitchenTicketId",
                schema: "pos",
                table: "KitchenTicketLines",
                column: "KitchenTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTicketLines_PosOrderLineId",
                schema: "pos",
                table: "KitchenTicketLines",
                column: "PosOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTickets_KitchenStationId",
                schema: "pos",
                table: "KitchenTickets",
                column: "KitchenStationId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTickets_PosOrderId_SendNumber_KitchenStationId",
                schema: "pos",
                table: "KitchenTickets",
                columns: new[] { "PosOrderId", "SendNumber", "KitchenStationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosAreas_BillingLocationId",
                schema: "pos",
                table: "PosAreas",
                column: "BillingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PosAreas_OrganizationId_BillingLocationId_Name",
                schema: "pos",
                table: "PosAreas",
                columns: new[] { "OrganizationId", "BillingLocationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosOrderLines_KitchenStationId",
                schema: "pos",
                table: "PosOrderLines",
                column: "KitchenStationId");

            migrationBuilder.CreateIndex(
                name: "IX_PosOrderLines_PosOrderId_LineNo",
                schema: "pos",
                table: "PosOrderLines",
                columns: new[] { "PosOrderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosOrderLines_ProductId",
                schema: "pos",
                table: "PosOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PosOrderLines_UnitId",
                schema: "pos",
                table: "PosOrderLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_BillingLocationId",
                schema: "pos",
                table: "PosOrders",
                column: "BillingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_ContactId",
                schema: "pos",
                table: "PosOrders",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_OrganizationId_BillingLocationId_Status",
                schema: "pos",
                table: "PosOrders",
                columns: new[] { "OrganizationId", "BillingLocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_OrganizationId_Code",
                schema: "pos",
                table: "PosOrders",
                columns: new[] { "OrganizationId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_OrganizationId_CreatedAt",
                schema: "pos",
                table: "PosOrders",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_OrganizationId_Date",
                schema: "pos",
                table: "PosOrders",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_OrganizationId_PosTableId",
                schema: "pos",
                table: "PosOrders",
                columns: new[] { "OrganizationId", "PosTableId" },
                unique: true,
                filter: "[Status] = 'Open' AND [PosTableId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PosOrders_PosTableId",
                schema: "pos",
                table: "PosOrders",
                column: "PosTableId");

            migrationBuilder.CreateIndex(
                name: "IX_PosTables_BillingLocationId",
                schema: "pos",
                table: "PosTables",
                column: "BillingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PosTables_OrganizationId_BillingLocationId_Name",
                schema: "pos",
                table: "PosTables",
                columns: new[] { "OrganizationId", "BillingLocationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosTables_PosAreaId",
                schema: "pos",
                table: "PosTables",
                column: "PosAreaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_KitchenStations_KitchenStationId",
                schema: "catalog",
                table: "Products",
                column: "KitchenStationId",
                principalSchema: "pos",
                principalTable: "KitchenStations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_KitchenStations_KitchenStationId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropTable(
                name: "KitchenTicketLines",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "KitchenTickets",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosOrderLines",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "KitchenStations",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosOrders",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosTables",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosAreas",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "IX_Products_KitchenStationId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e7"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e8"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e9"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ea"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001eb"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ec"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ed"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ee"));

            migrationBuilder.DropColumn(
                name: "KitchenStationId",
                schema: "catalog",
                table: "Products");
        }
    }
}
