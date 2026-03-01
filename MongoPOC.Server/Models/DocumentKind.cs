namespace MongoPOC.Server.Models;

public enum DocumentKind
{
    DJSON,
    FormData,
    FormDataPreview
}

public static class DocumentKindExtensions
{
    public static readonly DocumentKind[] All =
    [
        DocumentKind.DJSON,
        DocumentKind.FormData,
        DocumentKind.FormDataPreview
    ];

    public static bool TryParse(string? value, out DocumentKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("DJSON", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocumentKind.DJSON;
            return true;
        }

        if (value.Equals("FormData", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocumentKind.FormData;
            return true;
        }

        if (value.Equals("FormDataPreview", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocumentKind.FormDataPreview;
            return true;
        }

        return false;
    }

    public static string ToKindValue(this DocumentKind kind) => kind switch
    {
        DocumentKind.DJSON => "DJSON",
        DocumentKind.FormData => "FormData",
        DocumentKind.FormDataPreview => "FormDataPreview",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string ToFolderName(this DocumentKind kind) => kind.ToKindValue();

    public static string ToCollectionName(this DocumentKind kind) => kind switch
    {
        DocumentKind.DJSON => "djson_documents",
        DocumentKind.FormData => "form_data_documents",
        DocumentKind.FormDataPreview => "form_data_preview_documents",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
