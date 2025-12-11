using System;
using System.Collections.Generic;
using AasSharpClient.Models.Messages;

namespace MAS_BT.Nodes.ModuleHolon;

internal record CachedInventoryData(List<StorageUnit> StorageUnits, DateTime UpdatedAtUtc);

internal record CachedNeighborsData(List<string> Neighbors, DateTime UpdatedAtUtc);
