using System.Diagnostics;

namespace AbpStudioLinux.Installer.Infrastructure;

public sealed record ShellCommand(
    string FileName,
    IReadOnlyList<string> Arguments)
{
    public string ToDisplayString()
    {
        return string.Join(" ", new[] { FileName }.Concat(Arguments).Select(Quote));
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '/' or '.' or '-' or '_' or ':' or '='))
        {
            return value;
        }

        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}

public static class ProcessRunner
{
    public static int Run(
        ShellCommand command,
        TextWriter stdout,
        TextWriter stderr,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = new Process();
        process.StartInfo.FileName = command.FileName;

        foreach (var argument in command.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        if (workingDirectory is not null)
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.WriteLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }

    public static int RunInteractive(
        ShellCommand command,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = new Process();
        process.StartInfo.FileName = command.FileName;

        foreach (var argument in command.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.UseShellExecute = false;

        if (workingDirectory is not null)
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }
}
