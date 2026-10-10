using System.Text.RegularExpressions;

namespace BoardGame.Authoring;

public static class AuthorSkillTemplate
{
    public static string Render(string template, string cli, string docs)
    {
        if (string.IsNullOrWhiteSpace(cli) || string.IsNullOrWhiteSpace(docs)) throw new InvalidDataException("skill.variables: CLI and documentation paths are required");
        cli=Path.GetFullPath(cli);docs=Path.GetFullPath(docs);
        if (!File.Exists(cli) || !File.Exists(Path.Combine(docs,"entry.md"))) throw new InvalidDataException("skill.paths: CLI or bundled author documents are missing");
        if (cli.Any(char.IsControl) || docs.Any(char.IsControl)) throw new InvalidDataException("skill.paths: control characters are unsupported");
        var variables = new Dictionary<string,string>
        {
            ["CLI_PS"]="'"+cli.Replace("'","''")+"'",
            ["DOCS_LINK"]="[本地作者文档](<"+Path.Combine(docs,"entry.md").Replace('\\','/').Replace("%","%25").Replace("#","%23")+">)"
        };
        if (!variables.Keys.All(key=>template.Contains("{{"+key+"}}",StringComparison.Ordinal))) throw new InvalidDataException("skill.template: missing required placeholders");
        var rendered=Regex.Replace(template,@"\{\{([^{}]+)\}\}",match=>variables.TryGetValue(match.Groups[1].Value,out var value)?value:throw new InvalidDataException("skill.template: unsupported placeholder "+match.Value));
        if(rendered.Contains("{{") || rendered.Contains("}}"))throw new InvalidDataException("skill.template: unresolved placeholders");
        if(!rendered.StartsWith("---\n",StringComparison.Ordinal) && !rendered.StartsWith("---\r\n",StringComparison.Ordinal))throw new InvalidDataException("skill.template: YAML frontmatter required");
        if(!Regex.IsMatch(rendered,@"(?m)^name: bgb-author\r?$"))throw new InvalidDataException("skill.template: expected bgb-author identity");
        return rendered;
    }
}
