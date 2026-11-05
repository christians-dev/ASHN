using Ashen.Reviews.Domain;

namespace Ashen.UnitTests.Reviews;

public class ReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_must_be_1_to_5(int rating)
    {
        var result = Review.Write(Guid.NewGuid(), Guid.NewGuid(), "Sam L.", rating, null, Now);

        Assert.True(Assert.IsType<Ashen.BuildingBlocks.Error>(result.Error).Fields!.ContainsKey("rating"));
    }

    [Fact]
    public void Text_is_trimmed_optional_and_at_most_2000_characters()
    {
        Assert.Null(Review.Write(Guid.NewGuid(), Guid.NewGuid(), "Sam L.", 5, "   ", Now).Value.Text);
        Assert.Equal("Great", Review.Write(Guid.NewGuid(), Guid.NewGuid(), "Sam L.", 5, " Great ", Now).Value.Text);
        Assert.True(Review.Write(Guid.NewGuid(), Guid.NewGuid(), "Sam L.", 5, new string('a', 2000), Now).IsSuccess);
        Assert.False(Review.Write(Guid.NewGuid(), Guid.NewGuid(), "Sam L.", 5, new string('a', 2001), Now).IsSuccess);
    }

    [Fact]
    public void Hide_and_unhide()
    {
        var review = Review.Write(Guid.NewGuid(), Guid.NewGuid(), "Sam L.", 4, null, Now).Value;

        review.Hide();
        Assert.True(review.IsHidden);
        review.Unhide();
        Assert.False(review.IsHidden);
    }

    [Theory]
    [InlineData("Sam Lee", "Sam L.")]
    [InlineData("Mary Ann van der berg", "Mary B.")]
    [InlineData("Cher", "Cher")]
    [InlineData(null, "Customer")]
    public void Public_name_shows_first_name_and_last_initial(string? displayName, string expected)
    {
        Assert.Equal(expected, Review.PublicName(displayName));
    }
}
