using AasSharpClient.Models;

namespace MAS_BT.Nodes.Configuration;

/// <summary>
/// Loads the BillOfMaterial submodel and instantiates the typed AAS-Sharp representation.
/// </summary>
public class LoadBillOfMaterialSubmodelNode : LoadSubmodelNodeBase<BillOfMaterialSubmodel>
{
    public LoadBillOfMaterialSubmodelNode() : base("LoadBillOfMaterialSubmodel")
    {
    }

    protected override string DefaultIdShort => "BillOfMaterial";
    protected override string BlackboardKey => "BillOfMaterialSubmodel";

    protected override BillOfMaterialSubmodel CreateTypedInstance(string identifier)
    {
        return BillOfMaterialSubmodel.CreateWithIdentifier(identifier);
    }
}
