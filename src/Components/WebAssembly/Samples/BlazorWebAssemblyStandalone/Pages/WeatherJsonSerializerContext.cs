// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace BlazorWebAssemblyStandalone.Pages;

// Source-generated JSON metadata so deserialization is trim-safe (avoids IL2026 from reflection-based serialization).
[JsonSerializable(typeof(WeatherForecast[]))]
internal sealed partial class WeatherJsonSerializerContext : JsonSerializerContext
{
}
