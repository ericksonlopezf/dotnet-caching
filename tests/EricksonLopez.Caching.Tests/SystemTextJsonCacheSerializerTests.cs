// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Caching.Tests;

public sealed class SystemTextJsonCacheSerializerTests
{
    private sealed record SampleUser(int Id, string Name, bool IsActive);

    [Fact]
    public void Default_Instance_IsNotNullAndSingleton()
    {
        var instance1 = SystemTextJsonCacheSerializer.Default;
        var instance2 = SystemTextJsonCacheSerializer.Default;

        instance1.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void SerializeAndDeserialize_ComplexObject_RoundtripsAccurately()
    {
        var serializer = SystemTextJsonCacheSerializer.Default;
        var user = new SampleUser(42, "Erickson Lopez", true);

        var json = serializer.Serialize(user);
        var deserialized = serializer.Deserialize<SampleUser>(json);

        json.Should().NotBeNullOrWhiteSpace();
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(42);
        deserialized.Name.Should().Be("Erickson Lopez");
        deserialized.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SerializeToUtf8Bytes_AndDeserializeFromUtf8Bytes_RoundtripsAccurately()
    {
        var serializer = SystemTextJsonCacheSerializer.Default;
        var user = new SampleUser(100, "Jane Doe", false);

        var bytes = serializer.SerializeToUtf8Bytes(user);
        var deserialized = serializer.DeserializeFromUtf8Bytes<SampleUser>(bytes);

        bytes.Should().NotBeEmpty();
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(100);
        deserialized.Name.Should().Be("Jane Doe");
        deserialized.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Serialize_NullValue_ProducesNullJson()
    {
        var serializer = SystemTextJsonCacheSerializer.Default;

        var json = serializer.Serialize<string?>(null);
        var deserialized = serializer.Deserialize<string?>(json);

        json.Should().Be("null");
        deserialized.Should().BeNull();
    }

    [Fact]
    public void Deserialize_NullString_ReturnsDefault()
    {
        var serializer = SystemTextJsonCacheSerializer.Default;

        var result = serializer.Deserialize<SampleUser?>("null");

        result.Should().BeNull();
    }

    [Fact]
    public void DeserializeFromUtf8Bytes_NullPayload_ReturnsDefault()
    {
        var serializer = SystemTextJsonCacheSerializer.Default;
        var nullBytes = Encoding.UTF8.GetBytes("null");

        var result = serializer.DeserializeFromUtf8Bytes<SampleUser?>(nullBytes);

        result.Should().BeNull();
    }

    [Fact]
    public void CustomOptions_AreRespectedDuringSerialization()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        var serializer = new SystemTextJsonCacheSerializer(options);
        var user = new SampleUser(1, "Alice", true);

        var json = serializer.Serialize(user);

        json.Should().Contain("\"name\": \"Alice\"");
    }

    [Fact]
    public void Implements_ICacheSerializerContract()
    {
        var serializer = SystemTextJsonCacheSerializer.Default;

        var serialized = ((ICacheSerializer)serializer).Serialize("test string");
        var deserialized = ((ICacheSerializer)serializer).Deserialize<string>(serialized);

        deserialized.Should().Be("test string");
    }
}
