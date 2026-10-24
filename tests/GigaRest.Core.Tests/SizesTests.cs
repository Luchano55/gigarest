using GigaRest.Core.Text;

namespace GigaRest.Core.Tests;

public class SizesTests
{
    [Theory]
    [InlineData(0L, "0 МБ")]
    [InlineData(640L << 20, "640 МБ")]
    [InlineData(1288490188L, "1,2 ГБ")]
    [InlineData(3L << 30, "3,0 ГБ")]
    [InlineData(-5L, "0 МБ")]
    public void FormatsRussian(long bytes, string expected) => Assert.Equal(expected, Sizes.Format(bytes));

    [Theory]
    [InlineData(1, "1 программа")]
    [InlineData(3, "3 программы")]
    [InlineData(5, "5 программ")]
    [InlineData(11, "11 программ")]
    [InlineData(21, "21 программа")]
    [InlineData(112, "112 программ")]
    public void Plurals(int n, string expected) => Assert.Equal(expected, Sizes.Plural(n, "программа", "программы", "программ"));
}
