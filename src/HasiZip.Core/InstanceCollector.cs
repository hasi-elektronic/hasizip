using System.IO.Pipes;
using System.Text;

namespace HasiZip.Core;

/// <summary>
/// Explorer startet bei Mehrfachauswahl für jede markierte Datei einen eigenen Prozess.
/// Der erste Prozess wird „Sammler“, nimmt über eine Named Pipe die Pfade der anderen entgegen
/// und arbeitet alles gemeinsam ab (eine ZIP statt zehn). Die anderen Prozesse beenden sich sofort.
/// Läuft komplett synchron auf einem Thread, weil Mutex-Besitz an den Thread gebunden ist.
/// </summary>
public static class InstanceCollector
{
    /// <returns>
    /// Alle gesammelten Pfade (eigene zuerst), wenn dieser Prozess der Sammler ist;
    /// <c>null</c>, wenn die Pfade an einen laufenden Sammler übergeben wurden.
    /// </returns>
    public static IReadOnlyList<string>? Collect(string channel, IReadOnlyList<string> mine, TimeSpan quietPeriod)
    {
        var id = $"HasiZip.{channel}.{Environment.UserName}";
        var pipeName = Sanitize($"HasiZip-{channel}-{Environment.UserName}");
        using var mutex = new Mutex(false, Sanitize(id));

        for (int attempt = 0; attempt < 20; attempt++)
        {
            bool owner;
            try { owner = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owner = true; }

            if (owner)
            {
                try { return Serve(pipeName, mine, quietPeriod); }
                finally { mutex.ReleaseMutex(); }
            }
            if (TrySend(pipeName, mine)) return null;
            Thread.Sleep(50); // Sammler beendet gerade → gleich selbst Sammler werden
        }
        return mine; // Notfall: allein weitermachen
    }

    private static IReadOnlyList<string> Serve(string pipeName, IReadOnlyList<string> mine, TimeSpan quietPeriod)
    {
        var all = new List<string>(mine);
        var seen = new HashSet<string>(mine, SafePath.PathComparer);
        var server = CreateServer(pipeName);
        try
        {
            while (true)
            {
                using (var cts = new CancellationTokenSource(quietPeriod))
                {
                    try { server.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { return all; } // Ruhe → alle sind da
                }
                // Nächste Instanz anlegen, bevor die aktuelle schließt – sonst gehen wartende Verbindungen verloren
                var next = CreateServer(pipeName);
                using (var connected = server)
                using (var reader = new StreamReader(connected, Encoding.UTF8))
                {
                    server = next;
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length > 0 && seen.Add(line)) all.Add(line);
                    }
                }
            }
        }
        finally
        {
            server.Dispose();
        }
    }

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static bool TrySend(string pipeName, IReadOnlyList<string> mine)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            foreach (var p in mine) writer.WriteLine(p);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        return sb.ToString();
    }
}
