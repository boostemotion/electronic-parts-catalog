using System.Text.Json;

namespace ComponentCatalogCore;

/// <summary>
/// 提供全局共享的 <see cref="JsonSerializerOptions"/> 实例，避免各处重复创建。
/// </summary>
public static class CatalogJson
{
    /// <summary>
    /// 标准 camelCase + 缩进的序列化/反序列化选项。
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
