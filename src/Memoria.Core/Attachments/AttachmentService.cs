using System;
using System.IO;

namespace Memoria.Core.Attachments;

/// <summary>이미지 파일 저장/경로 해석/노트별 삭제. base = DataDirectory, 루트 = base/attachments.</summary>
public sealed class AttachmentService : IAttachmentService
{
    private readonly string _dataDir;
    public AttachmentService(string dataDirectory) => _dataDir = dataDirectory;

    private string Root => Path.Combine(_dataDir, "attachments");

    public string SaveImage(int noteId, byte[] bytes, string ext)
    {
        var name = Guid.NewGuid().ToString("N") + "." + ext.TrimStart('.');
        var dir = Path.Combine(Root, noteId.ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        return $"attachments/{noteId}/{name}";
    }

    public string SaveFile(int noteId, string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).TrimStart('.');
        var name = Guid.NewGuid().ToString("N") + "." + ext;
        var dir = Path.Combine(Root, noteId.ToString());
        Directory.CreateDirectory(dir);
        File.Copy(sourcePath, Path.Combine(dir, name));
        return $"attachments/{noteId}/{name}";
    }

    public string ResolveToAbsolute(string relativePath) =>
        Path.GetFullPath(Path.Combine(_dataDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public void DeleteForNote(int noteId)
    {
        var dir = Path.Combine(Root, noteId.ToString());
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
