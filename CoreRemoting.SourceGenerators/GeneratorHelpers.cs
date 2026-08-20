using System.Text;

namespace CoreRemoting.SourceGenerators;

public static class GeneratorHelpers
{
    public static string SanitizeFileName(string name)
    {
        if (name.StartsWith("global::"))
            name = name.Substring(8);

        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}
