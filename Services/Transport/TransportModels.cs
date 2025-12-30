using System.Collections.Generic;

namespace MAS_BT.Services.Transport;

public sealed record TransportEdge(string From, string To, double Distance);

public sealed record TransportLeg(string From, string To, double Distance, double Cost);

public sealed record TransportPlan(IReadOnlyList<TransportLeg> Legs, double TotalCost);
