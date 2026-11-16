using System.IO;
using UnityEditor;
using UnityEngine;

namespace FlightRecord.EditorTools
{
    /// <summary>
    /// Chooses the game's audio source for Play mode (<see cref="GameAudio"/>): the live
    /// bridge, an audio file the game plays with the sidecar analyzing it, a recorded message
    /// log, or none. Saved in PlayerPrefs; player builds use the command line instead.
    /// Takes effect the next time the game scene starts.
    /// </summary>
    static class AudioSourceMenu
    {
        const string Root = "Flight Record/Audio Source/";
        const string BridgeItem = Root + "Live Bridge (send.py)";
        const string FileItem = Root + "Audio File...";
        const string ReplayItem = Root + "Replay Log...";
        const string NoneItem = Root + "None";

        [MenuItem(BridgeItem, false, 0)]
        static void UseBridge() => Save(GameAudioSettings.SourceMode.Bridge, "");

        [MenuItem(FileItem, false, 1)]
        static void UseFile()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("Audio file for the game to play",
                StartFolder(Path.Combine("samples", "_gen")), new[] { "Audio (WAV, OGG, MP3)", "wav,ogg,mp3" });
            if (string.IsNullOrEmpty(path)) return;
            Save(GameAudioSettings.SourceMode.File, path);
            Debug.Log($"[GameAudio] Play mode will play {path}. Then run from the repo root:\n" +
                      $"python experiments/06-message-bridge/send.py --wav \"{path}\"");
        }

        [MenuItem(ReplayItem, false, 2)]
        static void UseReplay()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("Recorded message log",
                StartFolder(Path.Combine("samples", "_local", "logs")), new[] { "Message log", "jsonl" });
            if (string.IsNullOrEmpty(path)) return;
            Save(GameAudioSettings.SourceMode.Replay, path);
        }

        [MenuItem(NoneItem, false, 3)]
        static void UseNone() => Save(GameAudioSettings.SourceMode.None, "");

        [MenuItem(BridgeItem, true)]
        [MenuItem(FileItem, true)]
        [MenuItem(ReplayItem, true)]
        [MenuItem(NoneItem, true)]
        static bool Validate()
        {
            var mode = GameAudioSettings.Resolve(new string[0]).Mode;
            Menu.SetChecked(BridgeItem, mode == GameAudioSettings.SourceMode.Bridge);
            Menu.SetChecked(FileItem, mode == GameAudioSettings.SourceMode.File);
            Menu.SetChecked(ReplayItem, mode == GameAudioSettings.SourceMode.Replay);
            Menu.SetChecked(NoneItem, mode == GameAudioSettings.SourceMode.None);
            return true;
        }

        static void Save(GameAudioSettings.SourceMode mode, string path)
        {
            PlayerPrefs.SetString(GameAudioSettings.ModePref, mode.ToString());
            PlayerPrefs.SetString(GameAudioSettings.PathPref, path ?? "");
            PlayerPrefs.Save();
            Debug.Log($"[GameAudio] Audio source: {GameAudioSettings.Resolve(new string[0]).Describe()}");
        }

        /// <summary>A folder under the repo root (one above the Unity project), if it exists.</summary>
        static string StartFolder(string relative)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", relative));
            return Directory.Exists(dir) ? dir : Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        }
    }
}
