using System.IO;
using FluentAssertions;
using Memoria.Core.Attachments;
using Xunit;

namespace Memoria.Tests.Core;

public class AttachmentServiceTests
{
    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "memoria_att_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void SaveImage_WritesFile_AndReturnsRelativePath_ResolvableToAbsolute()
    {
        var dir = NewTempDir();
        try
        {
            var sut = new AttachmentService(dir);
            var rel = sut.SaveImage(7, new byte[] { 1, 2, 3 }, "png");

            rel.Should().StartWith("attachments/7/").And.EndWith(".png");
            var abs = sut.ResolveToAbsolute(rel);
            File.Exists(abs).Should().BeTrue();
            File.ReadAllBytes(abs).Should().Equal(1, 2, 3);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SaveFile_CopiesSource_PreservingExtension()
    {
        var dir = NewTempDir();
        try
        {
            var src = Path.Combine(dir, "src.jpg");
            File.WriteAllBytes(src, new byte[] { 9 });
            var sut = new AttachmentService(dir);

            var rel = sut.SaveFile(3, src);

            rel.Should().StartWith("attachments/3/").And.EndWith(".jpg");
            File.Exists(sut.ResolveToAbsolute(rel)).Should().BeTrue();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DeleteForNote_RemovesNoteFolder()
    {
        var dir = NewTempDir();
        try
        {
            var sut = new AttachmentService(dir);
            var rel = sut.SaveImage(5, new byte[] { 1 }, "png");
            sut.DeleteForNote(5);
            File.Exists(sut.ResolveToAbsolute(rel)).Should().BeFalse();
            Directory.Exists(Path.Combine(dir, "attachments", "5")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }
}
