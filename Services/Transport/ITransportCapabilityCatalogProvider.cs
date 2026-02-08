using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MAS_BT.Services.Transport;

public interface ITransportCapabilityCatalogProvider
{
    Task<TransportCapabilityCatalog> GetCatalogAsync(
        IEnumerable<string> moduleIds,
        CancellationToken cancellationToken = default);
}
