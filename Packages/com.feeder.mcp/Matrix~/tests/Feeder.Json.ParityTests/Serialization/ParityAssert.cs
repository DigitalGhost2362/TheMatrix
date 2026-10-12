namespace Feeder.Json.ParityTests.SerializerParity;

internal static class ParityAssert
{
    // long transcripts are easier to diff as files; the last failure is kept under %TEMP%\feeder-json-parity
    public static void Same(string expected, string actual, string name)
    {
        if (expected != actual)
        {
            string folder = Path.Combine(Path.GetTempPath(), "feeder-json-parity");
            Directory.CreateDirectory(folder);
            string safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            File.WriteAllText(Path.Combine(folder, safe + ".expected.txt"), expected);
            File.WriteAllText(Path.Combine(folder, safe + ".actual.txt"), actual);
        }
        Assert.Equal(expected, actual);
    }
}
