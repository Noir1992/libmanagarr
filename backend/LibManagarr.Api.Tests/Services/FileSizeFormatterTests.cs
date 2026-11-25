using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class FileSizeFormatterTests
{
    [Theory]
    [InlineData(0, SizeUnitSystem.Decimal, "0 B")]
    [InlineData(999, SizeUnitSystem.Decimal, "999 B")]
    [InlineData(1000, SizeUnitSystem.Decimal, "1 KB")]
    [InlineData(1000000, SizeUnitSystem.Decimal, "1 MB")]
    [InlineData(1000000000, SizeUnitSystem.Decimal, "1 GB")]
    [InlineData(1000000000000, SizeUnitSystem.Decimal, "1 TB")]
    [InlineData(1024, SizeUnitSystem.Binary, "1 KiB")]
    [InlineData(1048576, SizeUnitSystem.Binary, "1 MiB")]
    [InlineData(1073741824, SizeUnitSystem.Binary, "1 GiB")]
    [InlineData(1099511627776, SizeUnitSystem.Binary, "1 TiB")]
    [InlineData(536870912, SizeUnitSystem.Decimal, "536.87 MB")]
    [InlineData(536870912, SizeUnitSystem.Binary, "512 MiB")]
    public void Format_UsesMatchingUnitsAndBase(long bytes, SizeUnitSystem units, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes, units, AppLanguage.En));
    }

    [Theory]
    [InlineData(AppLanguage.En, "1.07 GB")]
    [InlineData(AppLanguage.De, "1,07 GB")]
    [InlineData(AppLanguage.Fr, "1,07 GB")]
    public void Format_UsesConfiguredLanguage(AppLanguage language, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(1073741824, SizeUnitSystem.Decimal, language));
    }
}
