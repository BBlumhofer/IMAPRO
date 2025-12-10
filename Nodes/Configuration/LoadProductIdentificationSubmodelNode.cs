using AasSharpClient.Models;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// Loads the ProductIdentification submodel for the current shell and maps it onto the strongly typed AAS-Sharp model.
/// </summary>
public class LoadProductIdentificationSubmodelNode : LoadSubmodelNodeBase<ProductIdentificationSubmodel>
{
    public LoadProductIdentificationSubmodelNode() : base("LoadProductIdentificationSubmodel")
    {
    }

    protected override string DefaultIdShort => "ProductIdentification";
    protected override string BlackboardKey => "ProductIdentificationSubmodel";

    protected override ProductIdentificationSubmodel CreateTypedInstance(string identifier)
    {
        return ProductIdentificationSubmodel.CreateWithIdentifier(identifier);
    }
}
