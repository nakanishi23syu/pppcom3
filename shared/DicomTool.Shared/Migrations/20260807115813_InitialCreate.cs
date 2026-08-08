using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DicomTool.Shared.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_study",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudyInstanceUid = table.Column<string>(type: "text", nullable: false),
                    PatientId = table.Column<string>(type: "text", nullable: false),
                    PatientName = table.Column<string>(type: "text", nullable: false),
                    StudyDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StudyDescription = table.Column<string>(type: "text", nullable: false),
                    Modality = table.Column<string>(type: "text", nullable: false),
                    AccessionNumber = table.Column<string>(type: "text", nullable: false),
                    BodyPartExamined = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_study", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SeriesInstanceUid = table.Column<string>(type: "text", nullable: false),
                    SeriesNumber = table.Column<string>(type: "text", nullable: false),
                    SeriesDescription = table.Column<string>(type: "text", nullable: false),
                    Modality = table.Column<string>(type: "text", nullable: false),
                    UserStudyId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_series_user_study_UserStudyId",
                        column: x => x.UserStudyId,
                        principalTable: "user_study",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sop",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SopInstanceUid = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    InstanceNumber = table.Column<string>(type: "text", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadByUserId = table.Column<string>(type: "text", nullable: true),
                    UserSeriesId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sop", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_sop_user_series_UserSeriesId",
                        column: x => x.UserSeriesId,
                        principalTable: "user_series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_Username",
                table: "app_user",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_series_Order",
                table: "user_series",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_user_series_SeriesInstanceUid",
                table: "user_series",
                column: "SeriesInstanceUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_series_UserStudyId",
                table: "user_series",
                column: "UserStudyId");

            migrationBuilder.CreateIndex(
                name: "IX_user_sop_IsRead",
                table: "user_sop",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_user_sop_Order",
                table: "user_sop",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_user_sop_SopInstanceUid",
                table: "user_sop",
                column: "SopInstanceUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sop_UserSeriesId",
                table: "user_sop",
                column: "UserSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_user_study_AccessionNumber",
                table: "user_study",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_user_study_Order",
                table: "user_study",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_user_study_PatientId",
                table: "user_study",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_user_study_PatientId_StudyDate",
                table: "user_study",
                columns: new[] { "PatientId", "StudyDate" });

            migrationBuilder.CreateIndex(
                name: "IX_user_study_StudyDate",
                table: "user_study",
                column: "StudyDate");

            migrationBuilder.CreateIndex(
                name: "IX_user_study_StudyInstanceUid",
                table: "user_study",
                column: "StudyInstanceUid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "user_sop");

            migrationBuilder.DropTable(
                name: "user_series");

            migrationBuilder.DropTable(
                name: "user_study");
        }
    }
}
