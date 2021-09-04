using System;
using System.Text.Json.Serialization;

namespace FPSBoostNotifier
{
    public class Output
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
