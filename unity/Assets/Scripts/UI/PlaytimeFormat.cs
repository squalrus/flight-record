using UnityEngine;

namespace FlightRecord
{
    public static class PlaytimeFormat
    {
        public static string ToClock(float totalSeconds)
        {
            int seconds = Mathf.RoundToInt(totalSeconds);
            int hours = seconds / 3600;
            int minutes = seconds / 60 % 60;
            int secs = seconds % 60;
            return hours > 0 ? $"{hours}:{minutes:D2}:{secs:D2}" : $"{minutes}:{secs:D2}";
        }
    }
}
