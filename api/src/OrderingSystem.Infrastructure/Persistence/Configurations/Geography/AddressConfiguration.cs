using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Geography;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Geography;

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Label).HasMaxLength(Lengths.AddressPart).IsRequired();
        builder.Property(a => a.Line1).HasMaxLength(Lengths.AddressLine).IsRequired();
        builder.Property(a => a.Building).HasMaxLength(Lengths.AddressPart);
        builder.Property(a => a.Floor).HasMaxLength(Lengths.AddressPart);
        builder.Property(a => a.Landmark).HasMaxLength(Lengths.AddressLine);

        builder.Property(a => a.Lat)
            .HasPrecision(Precision.CoordinatePrecision, Precision.CoordinateScale);
        builder.Property(a => a.Lng)
            .HasPrecision(Precision.CoordinatePrecision, Precision.CoordinateScale);

        builder.HasOne(a => a.User)
            .WithMany(u => u.Addresses)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Zone)
            .WithMany(z => z.Addresses)
            .HasForeignKey(a => a.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        // "At most one default per user" as a database rule rather than application discipline.
        // A filtered unique index is the only way to say that in SQL Server: unique across rows
        // where IsDefault is set, unconstrained everywhere else.
        builder.HasIndex(a => a.UserId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Addresses_OneDefaultPerUser");

        builder.HasIndex(a => new { a.UserId, a.IsDeleted });
    }
}
