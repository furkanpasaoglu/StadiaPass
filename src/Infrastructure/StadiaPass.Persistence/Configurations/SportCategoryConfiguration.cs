using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Persistence.Configurations;

internal sealed class SportCategoryConfiguration : IEntityTypeConfiguration<SportCategory>
{
    public void Configure(EntityTypeBuilder<SportCategory> builder)
    {
        builder.ToTable("sport_categories");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();

        builder.Property(category => category.Name).HasMaxLength(60).IsRequired();
        builder.Property(category => category.Description).HasMaxLength(255);
        builder.Property(category => category.IsActive).IsRequired();

        builder.HasIndex(category => category.Name).IsUnique();

        // Small, always-loaded-with-the-owner set: a primitive collection keeps it in one column instead of
        // paying for a join table.
        builder.PrimitiveCollection(category => category.AllowedVenueKinds)
            .HasColumnName("allowed_venue_kinds")
            .ElementType(element => element.HasConversion<string>().HasMaxLength(16))
            .IsRequired();

        builder.Metadata
            .FindProperty(nameof(SportCategory.AllowedVenueKinds))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(category => category.DomainEvents);
    }
}
