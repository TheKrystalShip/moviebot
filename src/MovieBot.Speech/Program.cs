using System.Net.Sockets;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using TheKrystalShip.Speech;
using TheKrystalShip.Speech.Engine;

namespace TheKrystalShip.MovieBot.Speech;

/// <summary>
/// This host's ears. It holds one whisper model and answers one socket.
/// </summary>
/// <remarks>
/// <para>
/// <b>It listens and never speaks.</b> No synthesiser is registered, so no ONNX runtime is loaded
/// and no voice files are installed — about a gigabyte this machine keeps. A surface that asks it to
/// synthesise is answered <c>Unavailable</c>, which the protocol defines as an absence rather than a
/// failure and every client already reads that way, so nothing above this has to know which kind of
/// host it is talking to.
/// </para>
/// <para>
/// <b>It is resident, and loads before it is asked.</b> The other host that embeds this engine is
/// socket-activated and unloads when idle, because it shares a machine with a fleet of game servers
/// that want the memory back. This machine runs MovieBot and nothing else, so the model has nothing
/// better to be spent on — and the alternative is paying a load, and the card's one-off pipeline
/// compile, in front of a room waiting for a film to pause.
/// </para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = new SpeechSettings();
        configuration.GetSection(SpeechSettings.Section).Bind(settings);

        using ILoggerFactory loggers = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.AddSystemdConsole();
        });

        ILogger logger = loggers.CreateLogger("MovieBot.Speech");

        // Proves the socket is bound, that something is listening on it, and that whatever answers
        // has a model loaded — none of which "the unit is active" proves on its own.
        if (args is ["--check"])
        {
            await using var asking = new SpeechClient(settings.SocketPath, logger);
            SpeechStatus? status = await asking.StatusAsync();

            if (status is null)
            {
                await Console.Error.WriteLineAsync(
                    $"moviebot-speech: nothing answered on {settings.SocketPath}");
                return 1;
            }

            Console.WriteLine($"hearing: {status.Hearing}");
            return status.Loaded ? 0 : 1;
        }

        using var stopping = new CancellationTokenSource();

        // systemd sends SIGTERM to stop a unit. Without this the runtime ends the process mid-request
        // and whoever asked waits out their own timeout instead of being told.
        using PosixSignalRegistration term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            stopping.Cancel();
        });

        using Socket? listener = Listen(settings, logger);
        if (listener is null) return 1;

        var options = new SpeechEngineOptions
        {
            ModelPath = settings.ModelPath,
            UseGpu = settings.UseGpu,
            NativeLibraryPath = settings.NativeLibraryPath,
            Accelerators = settings.Accelerators,
            AudioContextFrames = settings.AudioContextFrames,
            SocketPath = settings.SocketPath,

            // Held for the life of the process, and loaded before anyone asks. Both halves matter:
            // the first stops the second request paying for a load, the second stops the first one
            // paying for it, and on this card the first request also compiles the compute pipelines.
            IdleMinutes = 0,
            LoadAtStartup = true,
        };

        // No synthesiser is registered, so none is loaded and none of Kokoro is linked in at all.
        // Degradation goes to the log and nowhere else. There is no journal on this machine and
        // nothing polls a leaf here, so the engine's findings are read by whoever reads the unit.
        await new SpeechServer(listener, options, NullSpeechHealth.Instance, logger)
            .RunAsync(stopping.Token);

        return 0;
    }

    /// <summary>The listening socket, bound here because nothing else binds it.</summary>
    /// <remarks>
    /// There is no socket activation on this host: the daemon is resident and holds its model, so
    /// there is nothing for an activation to start and nothing that would be given back by exiting.
    /// </remarks>
    private static Socket? Listen(SpeechSettings settings, ILogger logger)
    {
        try
        {
            string path = settings.SocketPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // A socket file left behind by a process that did not exit cleanly refuses the bind, and
            // nothing else on this host writes this path.
            File.Delete(path);

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(path));
            socket.Listen(16);

            logger.LogInformation("Speech: listening on {Socket}", path);
            return socket;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Speech: could not listen on {Socket}", settings.SocketPath);
            return null;
        }
    }
}
