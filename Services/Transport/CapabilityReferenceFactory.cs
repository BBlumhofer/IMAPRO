using System;
using System.Collections.Generic;
using BaSyx.Models.AdminShell;
using MAS_BT.Tools;

namespace MAS_BT.Services.Transport;

public static class CapabilityReferenceFactory
{
    private sealed record Neo4jReferenceKey(string? Type, string? Value);

    public static bool TryParseNeo4jReferenceJson(string? json, out Reference reference)
    {
        reference = null!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        List<Neo4jReferenceKey>? keysDto;
        try
        {
            keysDto = JsonFacade.Deserialize<List<Neo4jReferenceKey>>(json);
        }
        catch
        {
            return false;
        }

        if (keysDto == null || keysDto.Count == 0)
        {
            return false;
        }

        var keys = new List<IKey>();
        foreach (var dto in keysDto)
        {
            var typeText = dto?.Type;
            var valueText = dto?.Value;
            if (string.IsNullOrWhiteSpace(typeText) || string.IsNullOrWhiteSpace(valueText))
            {
                continue;
            }

            if (!TryMapKeyType(typeText, out var keyType))
            {
                continue;
            }

            keys.Add(new Key(keyType, valueText!.Trim()));
        }

        if (keys.Count == 0)
        {
            return false;
        }

        reference = new Reference(keys)
        {
            Type = ReferenceType.ModelReference
        };
        return true;
    }

    private static bool TryMapKeyType(string typeText, out KeyType keyType)
    {
        keyType = default;
        if (string.IsNullOrWhiteSpace(typeText))
        {
            return false;
        }

        if (Enum.TryParse(typeText.Trim(), ignoreCase: true, out keyType))
        {
            return true;
        }

        if (string.Equals(typeText, "SubmodelElementCollection", StringComparison.OrdinalIgnoreCase))
        {
            keyType = KeyType.SubmodelElementCollection;
            return true;
        }

        return false;
    }
}
