using UnityEditor;
using UnityEngine;

// 에디터가 켜지면 Unity MCP 연결을 자동으로 켬 (폰에서 원격으로 작업할 때 Start 버튼을 누를 수 없어서)
[InitializeOnLoad]
static class McpAutoStart
{
    static McpAutoStart()
    {
        if (Application.isBatchMode) return;
        EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
        EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", false); // .mcp.json은 stdio 방식
        EditorApplication.delayCall += async () =>
        {
            try { MCPForUnity.Editor.Services.EditorConfigurationCache.Instance.SetUseHttpTransport(false); bool ok = await MCPForUnity.Editor.Services.MCPServiceLocator.Bridge.StartAsync(); Debug.Log("[McpAutoStart] bridge " + ok); }
            catch (System.Exception e) { Debug.LogWarning("[McpAutoStart] " + e.Message); }
        };
    }
}
