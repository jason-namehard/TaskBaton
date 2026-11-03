using System.IO;
using System.Media;
using System.Windows;

namespace AgentRelay.Desktop;

public static class NotificationSounds
{
    private static readonly Lazy<IReadOnlyDictionary<string,SoundPlayer>> Players=new(CreatePlayers);

    public static void Play(string sound)
    {
        switch(sound)
        {
            case "none":return;
            case "error":SystemSounds.Hand.Play();return;
            case "waterdrop":case "soft-piano":case "warm-chord":Players.Value[sound].Play();return;
            default:throw new ArgumentOutOfRangeException(nameof(sound),sound,"Unknown notification sound.");
        }
    }

    private static IReadOnlyDictionary<string,SoundPlayer> CreatePlayers()
    {
        var players=new Dictionary<string,SoundPlayer>(StringComparer.Ordinal);
        foreach(var sound in new[]{"waterdrop","soft-piano","warm-chord"})
        {
            using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/AgentRelay.Desktop;component/Assets/Sounds/"+sound+".wav")).Stream;
            var audio=new MemoryStream();resource.CopyTo(audio);audio.Position=0;
            // The player owns this in-memory stream for the application lifetime; Play is asynchronous.
            var player=new SoundPlayer(audio);player.Load();players.Add(sound,player);
        }
        return players;
    }
}
