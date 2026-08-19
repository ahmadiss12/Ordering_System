using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Platform;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Platform;

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RateLbpPerUsd)
            .HasPrecision(Precision.ExchangeRatePrecision, Precision.ExchangeRateScale);

        builder.HasOne(r => r.SetByUser)
            .WithMany()
            .HasForeignKey(r => r.SetByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Append-only, so the only query that matters is "the newest row not after this moment".
        builder.HasIndex(r => r.EffectiveFrom);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ExchangeRates_RatePositive", "[RateLbpPerUsd] > 0"));
    }
}
