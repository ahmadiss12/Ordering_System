using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Identity;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(Lengths.TokenHash).IsRequired();

        // Every refresh is a lookup by hash, so this index is on the hot path. Unique because two
        // rows sharing a hash would make reuse detection ambiguous.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Reuse detection revokes the whole family at once, which is a lookup by FamilyId.
        builder.HasIndex(t => t.FamilyId);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
