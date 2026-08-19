using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Infrastructure.Persistence.Configurations.Menu;

internal sealed class OptionConfiguration : IEntityTypeConfiguration<Option>
{
    public void Configure(EntityTypeBuilder<Option> builder)
    {
        builder.ToTable("Options");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(Lengths.EntityName).IsRequired();

        // Signed: zero for "no pickles", negative when a removal genuinely discounts the line.
        builder.Property(o => o.PriceDeltaUsd)
            .HasPrecision(Precision.MoneyPrecision, Precision.MoneyScale);

        builder.HasOne(o => o.OptionGroup)
            .WithMany(g => g.Options)
            .HasForeignKey(o => o.OptionGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => new { o.OptionGroupId, o.SortOrder });

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Options_MaxQuantityPositive", "[MaxQuantity] >= 1"));
    }
}
