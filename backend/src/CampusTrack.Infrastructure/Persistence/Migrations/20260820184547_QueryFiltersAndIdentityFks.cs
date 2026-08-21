using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QueryFiltersAndIdentityFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_roles_RoleId1",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_users_UserId1",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "IX_user_roles_RoleId1",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "IX_user_roles_UserId1",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "RoleId1",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "user_roles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoleId1",
                table: "user_roles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId1",
                table: "user_roles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId1",
                table: "user_roles",
                column: "RoleId1");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_UserId1",
                table: "user_roles",
                column: "UserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_roles_RoleId1",
                table: "user_roles",
                column: "RoleId1",
                principalTable: "roles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_users_UserId1",
                table: "user_roles",
                column: "UserId1",
                principalTable: "users",
                principalColumn: "Id");
        }
    }
}
