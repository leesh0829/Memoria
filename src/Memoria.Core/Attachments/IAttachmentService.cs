namespace Memoria.Core.Attachments;

public interface IAttachmentService
{
    string SaveImage(int noteId, byte[] bytes, string ext);
    string SaveFile(int noteId, string sourcePath);
    string ResolveToAbsolute(string relativePath);
    void DeleteForNote(int noteId);
}
