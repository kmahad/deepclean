using System.Globalization;
using DeepClean.Core.Utilities;

namespace DeepClean.Tests;

public class SizeFormatterTests
{
    public SizeFormatterTests()
    {
        // The formatter uses the current culture, so pin it for stable expectations.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void FormatBytes_ReportsZeroForUnusableInput(double bytes)
    {
        Assert.Equal("0 B", SizeFormatter.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1000, "1000 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1610612736, "1.5 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void FormatBytes_PicksTheLargestUnitThatFits(double bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.FormatBytes(bytes));
    }

    [Fact]
    public void FormatBytes_RespectsRequestedDecimals()
    {
        Assert.Equal("2 KB", SizeFormatter.FormatBytes(1536, decimals: 0));
        Assert.Equal("1.5 KB", SizeFormatter.FormatBytes(1536, decimals: 2));
    }

    [Fact]
    public void FormatBytes_ClampsToTheLargestKnownUnit()
    {
        var sixPebibytes = Math.Pow(1024, 6);

        Assert.Equal("1024 PB", SizeFormatter.FormatBytes(sixPebibytes));
    }

    [Fact]
    public void FormatBytes_SupportsLongAndUnsignedOverloads()
    {
        Assert.Equal("1.5 KB", SizeFormatter.FormatBytes(1536L));
        Assert.Equal("1.5 KB", SizeFormatter.FormatBytes(1536UL));
    }
}
