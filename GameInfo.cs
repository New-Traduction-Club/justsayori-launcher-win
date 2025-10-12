using System.Text.Json.Serialization;

namespace justsayo_win;

public class GameInfo
{
    [JsonPropertyName("id_proyecto")]
    public string? Id { get; set; }

    [JsonPropertyName("titulo")]
    public string? Title { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("url_descarga")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("nombre_ejecutable")]
    public string? ExecutableName { get; set; }
}
