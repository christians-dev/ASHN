using Ashen.BuildingBlocks.Data;
using Ashen.Reviews.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashen.Reviews.Data;

internal sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "reviews";

    public DbSet<Review> Reviews => Set<Review>();
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> review)
    {
        review.ToTable("reviews", t => t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
        review.HasXminConcurrencyToken();
        review.Property(r => r.AuthorDisplayName).HasMaxLength(100);
        review.Property(r => r.Text).HasMaxLength(Review.MaxTextLength);
        review.HasIndex(r => new { r.ProductId, r.CustomerId }).IsUnique(); // one review per customer per product
        review.HasIndex(r => new { r.ProductId, r.IsHidden, r.CreatedAt });
        review.HasIndex(r => r.CustomerId);
    }
}

internal sealed class ReviewsDbContextFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReviewsDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=ashen_design", ReviewsDbContext.SchemaName);
        return new ReviewsDbContext(options.Options);
    }
}
