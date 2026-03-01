namespace MongoPOC.Server.Models;

public enum DocumentKind
{
    DJSON,
    FormData,
    FormDataPreview
}

public static class DocumentKindExtensions
{
    public static readonly DocumentKind[] All = Enum.GetValues<DocumentKind>();

    public static bool TryParse(string? value, out DocumentKind kind)
        => Enum.TryParse(value, ignoreCase: true, out kind);

    public static string ToKindValue(this DocumentKind kind) => kind.ToString();

    public static string ToFolderName(this DocumentKind kind) => kind.ToKindValue();

    public static string ToCollectionName(this DocumentKind kind) => kind switch
    {
        DocumentKind.DJSON => "djson_documents",
        DocumentKind.FormData => "form_data_documents",
        DocumentKind.FormDataPreview => "form_data_preview_documents",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
