using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase25Manufacturing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "manufacturing");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultProductionCostAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillsOfMaterials",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ManufactureOnEverySale = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillsOfMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillsOfMaterials_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BomByProductLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillOfMaterialsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostAllocationPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomByProductLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BomByProductLines_BillsOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalSchema: "manufacturing",
                        principalTable: "BillsOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BomByProductLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BomExpenseLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillOfMaterialsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomExpenseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BomExpenseLines_BillsOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalSchema: "manufacturing",
                        principalTable: "BillsOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BomExpenseLines_CostTerms_CostTermId",
                        column: x => x.CostTermId,
                        principalSchema: "configuration",
                        principalTable: "CostTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BomRawMaterialLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillOfMaterialsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomRawMaterialLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BomRawMaterialLines_BillsOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalSchema: "manufacturing",
                        principalTable: "BillsOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BomRawMaterialLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionJournals",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillOfMaterialsId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReferrerType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReferrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RawMaterialCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ProductionExpenseCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CostAllocatedToByProduct = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    FinishedGoodsCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    FinishedGoodsUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionJournals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionJournals_BillsOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalSchema: "manufacturing",
                        principalTable: "BillsOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionJournals_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionJournals_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "tenancy",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BillOfMaterialsId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_BillsOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalSchema: "manufacturing",
                        principalTable: "BillsOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionJournalByProductLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostAllocationPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AllocatedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionJournalByProductLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionJournalByProductLines_ProductionJournals_ProductionJournalId",
                        column: x => x.ProductionJournalId,
                        principalSchema: "manufacturing",
                        principalTable: "ProductionJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionJournalByProductLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionJournalExpenseLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionJournalExpenseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionJournalExpenseLines_CostTerms_CostTermId",
                        column: x => x.CostTermId,
                        principalSchema: "configuration",
                        principalTable: "CostTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionJournalExpenseLines_ProductionJournals_ProductionJournalId",
                        column: x => x.ProductionJournalId,
                        principalSchema: "manufacturing",
                        principalTable: "ProductionJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionJournalRawMaterialLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConsumedUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionJournalRawMaterialLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionJournalRawMaterialLines_ProductionJournals_ProductionJournalId",
                        column: x => x.ProductionJournalId,
                        principalSchema: "manufacturing",
                        principalTable: "ProductionJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionJournalRawMaterialLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderByProductLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostAllocationPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderByProductLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrderByProductLines_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "manufacturing",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionOrderByProductLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderExpenseLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderExpenseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrderExpenseLines_CostTerms_CostTermId",
                        column: x => x.CostTermId,
                        principalSchema: "configuration",
                        principalTable: "CostTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrderExpenseLines_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "manufacturing",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderRawMaterialLines",
                schema: "manufacturing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderRawMaterialLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrderRawMaterialLines_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "manufacturing",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionOrderRawMaterialLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000155"), true, "Manufacturing.BillOfMaterials.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000156"), true, "Manufacturing.BillOfMaterials.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000157"), true, "Manufacturing.BillOfMaterials.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000158"), false, "Manufacturing.BillOfMaterials.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000159"), true, "Manufacturing.ProductionOrder.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000015a"), true, "Manufacturing.ProductionOrder.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000015b"), true, "Manufacturing.ProductionOrder.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000015c"), true, "Manufacturing.ProductionOrder.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000015d"), true, "Manufacturing.ProductionOrder.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000015e"), true, "Manufacturing.ProductionOrder.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000015f"), true, "Manufacturing.ProductionOrder.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000160"), false, "Manufacturing.ProductionOrder.Approve", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000161"), true, "Manufacturing.ProductionOrder.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000162"), false, "Manufacturing.ProductionOrder.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000163"), true, "Manufacturing.ProductionJournal.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000164"), true, "Manufacturing.ProductionJournal.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000165"), true, "Manufacturing.ProductionJournal.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000166"), true, "Manufacturing.ProductionJournal.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000167"), true, "Manufacturing.ProductionJournal.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000168"), true, "Manufacturing.ProductionJournal.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000169"), true, "Manufacturing.ProductionJournal.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000016a"), false, "Manufacturing.ProductionJournal.Approve", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000016b"), true, "Manufacturing.ProductionJournal.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000016c"), false, "Manufacturing.ProductionJournal.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000016d"), true, "Manufacturing.ProductionReport.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000016e"), true, "Manufacturing.ProductionReport.View", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillsOfMaterials_OrganizationId_ProductId",
                schema: "manufacturing",
                table: "BillsOfMaterials",
                columns: new[] { "OrganizationId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillsOfMaterials_ProductId",
                schema: "manufacturing",
                table: "BillsOfMaterials",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BomByProductLines_BillOfMaterialsId",
                schema: "manufacturing",
                table: "BomByProductLines",
                column: "BillOfMaterialsId");

            migrationBuilder.CreateIndex(
                name: "IX_BomByProductLines_ProductId",
                schema: "manufacturing",
                table: "BomByProductLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BomExpenseLines_BillOfMaterialsId",
                schema: "manufacturing",
                table: "BomExpenseLines",
                column: "BillOfMaterialsId");

            migrationBuilder.CreateIndex(
                name: "IX_BomExpenseLines_CostTermId",
                schema: "manufacturing",
                table: "BomExpenseLines",
                column: "CostTermId");

            migrationBuilder.CreateIndex(
                name: "IX_BomRawMaterialLines_BillOfMaterialsId",
                schema: "manufacturing",
                table: "BomRawMaterialLines",
                column: "BillOfMaterialsId");

            migrationBuilder.CreateIndex(
                name: "IX_BomRawMaterialLines_ProductId",
                schema: "manufacturing",
                table: "BomRawMaterialLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournalByProductLines_ProductId",
                schema: "manufacturing",
                table: "ProductionJournalByProductLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournalByProductLines_ProductionJournalId",
                schema: "manufacturing",
                table: "ProductionJournalByProductLines",
                column: "ProductionJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournalExpenseLines_CostTermId",
                schema: "manufacturing",
                table: "ProductionJournalExpenseLines",
                column: "CostTermId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournalExpenseLines_ProductionJournalId",
                schema: "manufacturing",
                table: "ProductionJournalExpenseLines",
                column: "ProductionJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournalRawMaterialLines_ProductId",
                schema: "manufacturing",
                table: "ProductionJournalRawMaterialLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournalRawMaterialLines_ProductionJournalId",
                schema: "manufacturing",
                table: "ProductionJournalRawMaterialLines",
                column: "ProductionJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournals_BillOfMaterialsId",
                schema: "manufacturing",
                table: "ProductionJournals",
                column: "BillOfMaterialsId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournals_ProductId",
                schema: "manufacturing",
                table: "ProductionJournals",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournals_WarehouseId",
                schema: "manufacturing",
                table: "ProductionJournals",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderByProductLines_ProductId",
                schema: "manufacturing",
                table: "ProductionOrderByProductLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderByProductLines_ProductionOrderId",
                schema: "manufacturing",
                table: "ProductionOrderByProductLines",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderExpenseLines_CostTermId",
                schema: "manufacturing",
                table: "ProductionOrderExpenseLines",
                column: "CostTermId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderExpenseLines_ProductionOrderId",
                schema: "manufacturing",
                table: "ProductionOrderExpenseLines",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderRawMaterialLines_ProductId",
                schema: "manufacturing",
                table: "ProductionOrderRawMaterialLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderRawMaterialLines_ProductionOrderId",
                schema: "manufacturing",
                table: "ProductionOrderRawMaterialLines",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_BillOfMaterialsId",
                schema: "manufacturing",
                table: "ProductionOrders",
                column: "BillOfMaterialsId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ProductId",
                schema: "manufacturing",
                table: "ProductionOrders",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BomByProductLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "BomExpenseLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "BomRawMaterialLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionJournalByProductLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionJournalExpenseLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionJournalRawMaterialLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionOrderByProductLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionOrderExpenseLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionOrderRawMaterialLines",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionJournals",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "ProductionOrders",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "BillsOfMaterials",
                schema: "manufacturing");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000155"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000156"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000157"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000158"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000159"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000015a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000015b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000015c"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000015d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000015e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000015f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000160"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000161"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000162"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000163"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000164"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000165"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000166"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000167"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000168"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000169"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000016a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000016b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000016c"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000016d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000016e"));

            migrationBuilder.DropColumn(
                name: "DefaultProductionCostAccountId",
                schema: "tenancy",
                table: "TenantSettings");
        }
    }
}
