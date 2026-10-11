namespace Mailpane.Web.Pages;

public static class AccountColor
{
    /// <summary>CSS class for an account colour such as <c>#1a73e8</c>; see app.css.</summary>
    public static string CssClass(string color) => "c-" + color.TrimStart('#');
}
