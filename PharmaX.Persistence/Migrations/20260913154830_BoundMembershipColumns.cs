using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoundMembershipColumns : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// EF warns that narrowing a text column may lose data. It
        /// cannot here: MembershipNumber and DependantCode were added by
        /// the migration immediately before this one and no installation
        /// has had the chance to put anything in them. They are bounded
        /// because a membership number goes on every claim, and an
        /// unbounded column cannot be indexed.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
