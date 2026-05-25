namespace AbpStudioLinux.Installer.Cli;

public sealed class OptionReader
{
    private readonly IReadOnlyList<string> _args;

    public OptionReader(IReadOnlyList<string> args)
    {
        _args = args;
    }

    public bool Has(string name) => _args.Contains(name, StringComparer.Ordinal);

    public string? Value(string name)
    {
        for (var index = 0; index < _args.Count; index++)
        {
            var arg = _args[index];

            if (arg == name)
            {
                if (index + 1 >= _args.Count)
                {
                    throw new ArgumentException($"{name} requires a value");
                }

                return _args[index + 1];
            }

            var prefix = name + "=";

            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }
}
