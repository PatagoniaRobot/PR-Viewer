using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class InspectionSourceTests : IDisposable
{
    private readonly TestPackage _package = new();

    public void Dispose() => _package.Dispose();

    [Fact]
    public void DetectsZipByContentDespiteWrongExtension()
    {
        // La detección es por firma de bytes, no por extensión.
        var zipPath = _package.CreateZip("export.bin", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8("[25/6/2024, 14:03:12] Ana: Hola"),
        });

        using var source = InspectionSource.Open(zipPath);

        Assert.IsType<ZipInspectionSource>(source);
        Assert.Single(source.Entries);
        Assert.Equal("_chat.txt", source.Entries[0].Name);
    }

    [Fact]
    public void NonZipFileOpensAsSingleFile()
    {
        var filePath = _package.CreateFile("_chat.txt", TestPackage.Utf8("[25/6/2024, 14:03:12] Ana: Hola"));

        using var source = InspectionSource.Open(filePath);

        Assert.IsType<SingleFileInspectionSource>(source);
        Assert.Single(source.Entries);
    }

    [Fact]
    public void FolderSourceListsFilesRecursively()
    {
        var folderPath = _package.CreateFolder("export", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8("hola"),
            ["media/IMG-0001.jpg"] = new byte[] { 1, 2, 3 },
        });

        using var source = InspectionSource.Open(folderPath);

        Assert.IsType<FolderInspectionSource>(source);
        Assert.Equal(2, source.Entries.Count);
        Assert.Contains(source.Entries, e => e.Path == "media/IMG-0001.jpg" && e.Size == 3);
    }

    [Fact]
    public void EntryStreamsAreReadOnly()
    {
        var zipPath = _package.CreateZip("export.zip", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8("contenido"),
        });

        using var source = InspectionSource.Open(zipPath);
        using var stream = source.OpenRead(source.Entries[0]);

        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
    }
}
