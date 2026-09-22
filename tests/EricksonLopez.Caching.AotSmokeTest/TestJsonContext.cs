// Copyright © Erickson Lopez. MIT License.
using System.Text.Json.Serialization;

namespace EricksonLopez.Caching.AotSmokeTest;

[JsonSerializable(typeof(TestPayload))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}
