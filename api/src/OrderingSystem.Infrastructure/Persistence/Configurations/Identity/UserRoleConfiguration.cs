using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");

        // The composite key is the uniqueness rule: a user cannot hold the same role twice, and
        // the database refuses it rather than the application remembering to check.
        builder.HasKey(r => new { r.UserId, r.Role });

        builder.Property(r => r.Role).HasConversion<int>();

        builder.HasOne(r => r.User)
            .WithMany(u => u.Roles)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
