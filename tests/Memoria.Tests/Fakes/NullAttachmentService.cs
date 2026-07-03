using Memoria.Core.Attachments;

namespace Memoria.Tests.Fakes;

/// <summary>테스트용 no-op IAttachmentService 구현체. 파일 I/O 없음.</summary>
public sealed class NullAttachmentService : IAttachmentService
{
    public string SaveImage(int noteId, byte[] bytes, string ext) => $"attachments/{noteId}/test.{ext}";
    public string SaveFile(int noteId, string sourcePath) => $"attachments/{noteId}/test.bin";
    public string ResolveToAbsolute(string relativePath) => relativePath;
    public void DeleteForNote(int noteId) { }
}
