using System.Text.Json;
namespace AiLight.Core;

public static class HookWire {
    public static HookEvent? Read(string text) {
        if(text.Length>=16384)return null;
        try {
            var e=JsonSerializer.Deserialize<HookEvent>(text,new JsonSerializerOptions{MaxDepth=32});
            if(e is null || new[]{e.Id,e.Harness,e.SessionId,e.EventName,e.TurnId,e.ToolId,e.ToolName,e.Project,e.Notification,e.Correlation,e.Title,e.Source,e.MonitorRoot,e.ProducerId}.Any(s=>s is null))return null;
            if(string.IsNullOrWhiteSpace(e.Id)||!HarnessCatalog.Families.Contains(e.Harness))return null;
            if(string.IsNullOrWhiteSpace(e.SessionId)&&!(e.Harness=="opencode"&&e.EventName=="OpenCodeDisconnected"&&e.ProducerId.Length>0))return null;
            return e;
        }catch(JsonException){return null;}
    }
}
