using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// AOT（PublishAot=true）下 System.Text.Json 反射序列化被禁用，
    /// 必须使用源生成器上下文。
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(ArticleResult))]
    [JsonSerializable(typeof(FindResult))]
    [JsonSerializable(typeof(string))]
    internal partial class AppJsonContext : JsonSerializerContext
    {
    }
}
