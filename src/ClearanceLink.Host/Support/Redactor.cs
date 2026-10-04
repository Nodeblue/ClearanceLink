using System.Text.RegularExpressions;

namespace ClearanceLink.Host.Support;

/// <summary>
/// Blanks out who the user is before files go in a support file: the profile folder becomes %USERPROFILE% and the
/// Windows user name, as a whole word, becomes &lt;user&gt;.
/// </summary>
public sealed class Redactor
{
    private readonly string? _profile;
    private readonly Regex? _name;

    public Redactor(string? userProfile, string? userName)
    {
        _profile = string.IsNullOrWhiteSpace(userProfile) ? null : userProfile.TrimEnd('\\', '/');
        _name = string.IsNullOrWhiteSpace(userName) || userName.Length < 2
            ? null
            : new Regex($@"(?<![\w]){Regex.Escape(userName)}(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static Redactor ForCurrentUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

    public string Apply(string text)
    {
        if (_profile is not null)
        {
            text = text.Replace(_profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            text = text.Replace(_profile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        return _name is null ? text : _name.Replace(text, "<user>");
    }
}
