using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace FPSBoostNotifier
{
    public enum FPSBoost
    {
        NotAvailable = 0,
        Sixty = 60,
        OneTwenty = 120,
    };

    public class Game
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = String.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = String.Empty;

        [JsonPropertyName("series_x_fps")]
        public FPSBoost SeriesXFPS { get; set; }

        [JsonPropertyName("series_s_fps")]
        public FPSBoost SeriesSFPS { get; set; }

        [JsonPropertyName("off_by_default_series_x")]
        public bool OffByDefaultSeriesX { get; set; }

        internal bool HasGameChanged(Game otherGame)
        {
            return (this.Title != otherGame.Title ||
                this.Url != otherGame.Url ||
                this.SeriesXFPS != otherGame.SeriesXFPS ||
                this.SeriesSFPS != otherGame.SeriesSFPS ||
                this.OffByDefaultSeriesX != otherGame.OffByDefaultSeriesX);
        }
    }
}
