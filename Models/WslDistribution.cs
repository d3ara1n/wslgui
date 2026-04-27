namespace WslGui.Models;

public sealed record WslDistribution(
    string Name,
    bool IsDefault,
    string State,
    int? Version);

public sealed record WslCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Success => ExitCode == 0;

    public string Message
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(StandardError)
                ? StandardOutput
                : StandardError;

            return string.IsNullOrWhiteSpace(text)
                ? $"wsl.exe exited with code {ExitCode}."
                : text.Trim();
        }
    }
}
