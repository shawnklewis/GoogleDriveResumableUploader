using System.Text;

namespace Gdup.Config;

/// <summary>Minimal rclone-compatible INI: [section] headers and "key = value" lines.</summary>
public sealed class IniFile
{
    private readonly List<IniSection> _sections = [];

    public IReadOnlyList<IniSection> Sections => _sections;

    public IniSection? Get(string name) =>
        _sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public IniSection GetOrAdd(string name)
    {
        var section = Get(name);
        if (section is null)
        {
            section = new IniSection(name);
            _sections.Add(section);
        }
        return section;
    }

    public bool Remove(string name) =>
        _sections.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        IniSection? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                current = ini.GetOrAdd(line[1..^1].Trim());
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || current is null) continue;
            current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return ini;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var section in _sections)
        {
            sb.Append('[').Append(section.Name).Append("]\n");
            foreach (var (key, value) in section.Entries)
                sb.Append(key).Append(" = ").Append(value).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

public sealed class IniSection(string name)
{
    private readonly List<KeyValuePair<string, string>> _entries = [];

    public string Name { get; } = name;

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries;

    public string? this[string key]
    {
        get
        {
            var i = IndexOf(key);
            return i < 0 ? null : _entries[i].Value;
        }
        set
        {
            var i = IndexOf(key);
            if (value is null)
            {
                if (i >= 0) _entries.RemoveAt(i);
            }
            else if (i < 0)
            {
                _entries.Add(new(key, value));
            }
            else
            {
                _entries[i] = new(_entries[i].Key, value);
            }
        }
    }

    private int IndexOf(string key) =>
        _entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}
