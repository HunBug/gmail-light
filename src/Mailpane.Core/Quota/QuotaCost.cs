namespace Mailpane.Core.Quota;

/// <summary>
/// Quota units per method for projects created on or after 2026-05-01 (DISCOVERIES.md V-02,
/// GMAIL_API.md 3.2). Only the read methods milestone 1 uses.
/// </summary>
public static class QuotaCost
{
    public const int GetProfile = 1;
    public const int LabelsList = 1;
    public const int LabelsGet = 1;
    public const int HistoryList = 2;
    public const int ThreadsList = 10;
    public const int MessagesGet = 20;
    public const int AttachmentsGet = 20;
    public const int ThreadsGet = 40;
}
