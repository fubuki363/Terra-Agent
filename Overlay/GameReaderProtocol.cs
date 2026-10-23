using System.Text.Json;

namespace TerraWikiOverlay;

internal sealed record GameReaderRequest(int Protocol, int ProcessId, string Operation, string Query = "", int Maximum = 8,
    bool Personal = true, GameContextSelection? Selection = null);
internal sealed record GameReaderResponse(int Protocol, bool Success, JsonElement? Data, string Failure);
internal sealed record PlayerPosition(float X, float Y);
