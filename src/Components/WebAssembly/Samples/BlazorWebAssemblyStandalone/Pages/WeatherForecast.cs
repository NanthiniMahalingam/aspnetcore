// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Text.Json.Serialization;

namespace BlazorWebAssemblyStandalone.Pages;

public class WeatherForecast
{
    public DateOnly Date { get; set; }

    public int TemperatureC { get; set; }

    public string? Summary { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

[JsonSerializable(typeof(WeatherForecast[]))]
internal sealed partial class WeatherJsonContext : JsonSerializerContext
{
}
