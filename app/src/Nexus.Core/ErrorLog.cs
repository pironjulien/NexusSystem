using System.Globalization;

namespace Nexus.Core;

/// <summary>
/// The app's own error log: one timestamped line per event, what failed inside the app, never screen text. Capped at
/// about 1.6 MB: the old file becomes <c>.1</c> (one generation), like the other Nexus logs. Never throws: a log never
/// stops the app. It stays on the PC.
/// </summary>
public sealed class ErrorLog(string file)
{
    public const long MaxBytes = 1_618_000;
    private static readonly object Gate = new();

    public string File { get; } = file;

    public void Error(string where, Exception error) => Write($"{where}: {error}");

    public void Info(string text) => Write(text);

    public void Write(string text, DateTimeOffset? now = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > MaxBytes) System.IO.File.Move(File, File + ".1", overwrite: true);
                var stamp = (now ?? DateTimeOffset.Now).ToString("o", CultureInfo.InvariantCulture);
                System.IO.File.AppendAllText(File, $"{stamp} {text}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // a log never stops the app
        }
        catch (UnauthorizedAccessException)
        {
            // idem
        }
    }
}
