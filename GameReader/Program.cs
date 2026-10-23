using System.Text;
using System.Text.Json;
using TerraWikiOverlay;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
while (Console.ReadLine() is { } line)
{
    GameReaderResponse reply;
    try
    {
        if (line.Length > 16384) throw new InvalidDataException("请求过大。");
        GameReaderRequest request = JsonSerializer.Deserialize<GameReaderRequest>(line) ?? throw new InvalidDataException("无效请求。");
        if (request.Protocol != 1) throw new InvalidDataException("协议版本不匹配。");
        GameProcessLocator.WorkerProcessId = request.ProcessId;
        if (GameProcessLocator.Find()?.IsTmod != true) throw new InvalidOperationException("64 位读取器只接受 tModLoader 客户端。");
        object? data = null;
        string failure = "";
        bool success;
        switch (request.Operation)
        {
            case "state": success = TerrariaManagedMemoryContextReader.TryRead(request.Selection ?? GameContextSelection.All, out string context, out failure); data = context; break;
            case "objects": success = TerrariaWorldLocator.TryFindWorldObject(request.Query, request.Maximum, out var objects, out failure); data = objects; break;
            case "biome": success = TerrariaWorldLocator.TryFindBiome(request.Query, request.Maximum, out var biomes, out failure); data = biomes; break;
            case "storage": success = TerrariaWorldLocator.TryFindItem(request.Query, request.Maximum, request.Personal, out var items, out failure); data = items; break;
            case "position": success = TerrariaWorldLocator.TryReadPlayerTilePosition(out float x, out float y, out failure); data = new PlayerPosition(x, y); break;
            default: throw new InvalidDataException("未知读取操作。");
        }
        reply = new(1, success, data is null ? null : JsonSerializer.SerializeToElement(data, data.GetType()), failure);
    }
    catch (Exception exception) { reply = new(1, false, null, exception.GetBaseException().Message); }
    finally { GameProcessLocator.WorkerProcessId = null; }
    Console.WriteLine(JsonSerializer.Serialize(reply)); Console.Out.Flush();
}
