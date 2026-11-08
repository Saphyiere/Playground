namespace Playground.AI.Assist.Tests;

public sealed class KeyTests : IDisposable
{
    const string Key = "sk-ant-api03-Zx9_yW8-vU7tS6rQ5pO4nM3lK2jI1hG0fE9dC8bA7-a1B2";
    readonly string folder = Directory.CreateTempSubdirectory("assist-key-").FullName;
    readonly MemoryKeys store = new();

    string KeyFile(string text)
    {
        var path = Path.Combine(folder, AssistKey.FileName);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public async Task ImportAsync_KeepsTheKeyAndDeletesTheFile()
    {
        var path = KeyFile(Key + "\r\n");

        var import = await AssistKey.ImportAsync(path, store);

        Assert.Equal(new KeyImport(KeyImportResult.Saved, "sk-ant-…a1B2"), import);
        Assert.Equal(Key, store.Key);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ImportAsync_TakesTheKeyFromJsonToo()
    {
        var import = await AssistKey.ImportAsync(KeyFile($$"""{ "apiKey": "{{Key}}" }"""), store);

        Assert.Equal(KeyImportResult.Saved, import.Result);
        Assert.Equal(Key, store.Key);
    }

    [Theory]
    [InlineData("my key goes here")]
    [InlineData("{ \"key\": \"sk-ant-api03-Zx9_yW8-vU7tS6rQ5pO4nM3lK2jI1hG0f\" }")]
    [InlineData("{ broken")]
    public async Task ImportAsync_DeletesAFileWithoutAKeyAndSaysSo(string text)
    {
        var path = KeyFile(text);

        var import = await AssistKey.ImportAsync(path, store);

        Assert.Equal(KeyImportResult.NotAKey, import.Result);
        Assert.Null(store.Key);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ImportAsync_WithoutAFileChangesNothing()
    {
        var import = await AssistKey.ImportAsync(Path.Combine(folder, AssistKey.FileName), new MemoryKeys(Key));

        Assert.Equal(KeyImportResult.NoFile, import.Result);
    }

    [Fact]
    public void Mask_ShowsOnlyTheStartAndTheLastFour()
    {
        Assert.Equal("sk-ant-…a1B2", AssistKey.Mask(Key));
    }
}
