using GigaRest.Core.Desktop;

namespace GigaRest.Core.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\Google\Chrome\Application\chrome.exe"" --profile-directory=""Profile 1""",
        new[] { @"C:\Program Files\Google\Chrome\Application\chrome.exe", "--profile-directory=Profile 1" })]
    [InlineData(@"C:\app.exe a b  c", new[] { @"C:\app.exe", "a", "b", "c" })]
    [InlineData(@"""C:\Мои программы\тест 1\прог.exe"" ""D:\Мои фото\тест 1.heic""",
        new[] { @"C:\Мои программы\тест 1\прог.exe", @"D:\Мои фото\тест 1.heic" })]
    [InlineData(@"app.exe ""a\""b"" c\\d ""e\\"" f", new[] { "app.exe", "a\"b", @"c\\d", @"e\", "f" })]
    [InlineData(@"app.exe ""he said """"hi""""""", new[] { "app.exe", "he said \"hi\"" })]
    [InlineData(@"app.exe """"", new[] { "app.exe", "" })]
    [InlineData(@"""C:\dir\""app.exe x", new[] { @"C:\dir\app.exe", "x" })]
    public void SplitsLikeWindows(string line, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Split(line));
    }

    [Fact]
    public void EmptyGivesNothing()
    {
        Assert.Empty(CommandLine.Split(null));
        Assert.Empty(CommandLine.Split(""));
    }
}
