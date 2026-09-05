using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(Lengths.Email).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(Lengths.PasswordHash).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(Lengths.PersonName).IsRequired();
        builder.Property(u => u.Phone).HasMaxLength(Lengths.Phone).IsRequired();

        // Defaults to false in the database as well as in the model, so every account that
        // existed before invitations reads as "has a password", which is true of all of them.
        builder.Property(u => u.MustSetPassword).HasDefaultValue(false);

        // Login looks users up by email on every attempt, and two accounts sharing one address
        // would make "which one?" unanswerable. Emails are stored lowercased by the use case.
        builder.HasIndex(u => u.Email).IsUnique();
    }
}
