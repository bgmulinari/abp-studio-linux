namespace AbpStudioLinux.Installer;

public static class Program
{
    public static async Task<int> Main(string[] args) =>
        await CliApplication.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
}
