using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MAS_BT.Services.Transport;

public interface ITransportGraphQuery
{
    Task<IReadOnlyList<TransportEdge>> GetShortestPathAsync(
        string fromModuleId,
        string toModuleId,
        CancellationToken cancellationToken = default);
}
