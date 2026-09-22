// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Caching;

/// <summary>
/// Defines an abstraction for serializing and deserializing cached objects for distributed cache stores.
/// </summary>
public interface ICacheSerializer
{
    /// <summary>
    /// Serializes the specified value to a string representation.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>A string representation of the serialized value.</returns>
    string Serialize<T>(T value);

    /// <summary>
    /// Deserializes a string representation into an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target object type.</typeparam>
    /// <param name="value">The serialized string value.</param>
    /// <returns>The deserialized object, or <see langword="null"/> if the payload represents null.</returns>
    T? Deserialize<T>(string value);

    /// <summary>
    /// Serializes the specified value to a UTF-8 byte array.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>A UTF-8 encoded byte array representing the serialized value.</returns>
    byte[] SerializeToUtf8Bytes<T>(T value);

    /// <summary>
    /// Deserializes a UTF-8 byte array into an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target object type.</typeparam>
    /// <param name="bytes">The UTF-8 encoded bytes.</param>
    /// <returns>The deserialized object, or <see langword="null"/> if the payload represents null.</returns>
    T? DeserializeFromUtf8Bytes<T>(byte[] bytes);
}
