// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace EricksonLopez.Caching;

/// <summary>
/// Default <see cref="ICacheSerializer"/> implementation using <see cref="JsonSerializer"/>.
/// </summary>
public sealed class SystemTextJsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Gets the default singleton instance configured with default serialization options.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Default JSON serialization. For strict AOT, provide a custom instance configured with a JsonSerializerContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Default JSON serialization. For strict AOT, provide a custom instance configured with a JsonSerializerContext.")]
    public static SystemTextJsonCacheSerializer Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonCacheSerializer"/> class.
    /// </summary>
    /// <param name="options">Optional custom <see cref="JsonSerializerOptions"/>.</param>
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Default JSON serialization. For strict AOT, provide a custom instance configured with a JsonSerializerContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Default JSON serialization. For strict AOT, provide a custom instance configured with a JsonSerializerContext.")]
    public SystemTextJsonCacheSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? JsonSerializerOptions.Default;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    public string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, _options);

    /// <inheritdoc />
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    public T? Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, _options);

    /// <inheritdoc />
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    public byte[] SerializeToUtf8Bytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, _options);

    /// <inheritdoc />
    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Default JSON serialization. For strict AOT, register a JsonSerializerContext on options.")]
    public T? DeserializeFromUtf8Bytes<T>(byte[] bytes) =>
        JsonSerializer.Deserialize<T>(bytes, _options);
}
