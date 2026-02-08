using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AasSharpClient.Models;
using BaSyx.Models.AdminShell;
using Xunit;
using Xunit.Abstractions;

namespace MAS_BT.Tests;

public class CapabilityComposedOfTests
{
    private readonly ITestOutputHelper _output;

    public CapabilityComposedOfTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void Log(string format, params object[] args)
    {
        var message = string.Format(format, args);
        _output.WriteLine(message);
        Console.WriteLine(message);
    }

    [Fact]
    public void ComposedOf_Decomposes_To_AtomicCapabilities()
    {
        const string submodelId = "https://example.org/submodels/test_composed";

        var parentContainer = new CapabilityContainerDefinition(
            "DockRetrieveHandoverContainer",
            new CapabilityElementDefinition("DockRetrieveHandoverCapability"));

        var child1 = new CapabilityContainerDefinition(
            "RetrieveContainer",
            new CapabilityElementDefinition("Retrieve"));

        var child2 = new CapabilityContainerDefinition(
            "DockingContainer",
            new CapabilityElementDefinition("Docking"));

        var parentRef = new Reference(new List<IKey>
        {
            new Key(KeyType.Submodel, submodelId),
            new Key(KeyType.SubmodelElementCollection, "CapabilitySet"),
            new Key(KeyType.SubmodelElementCollection, "DockRetrieveHandoverContainer"),
            new Key(KeyType.Capability, "DockRetrieveHandoverCapability")
        }) { Type = ReferenceType.ModelReference };

        var child1Ref = new Reference(new List<IKey>
        {
            new Key(KeyType.Submodel, submodelId),
            new Key(KeyType.SubmodelElementCollection, "CapabilitySet"),
            new Key(KeyType.SubmodelElementCollection, "RetrieveContainer"),
            new Key(KeyType.Capability, "Retrieve")
        }) { Type = ReferenceType.ModelReference };

        var child2Ref = new Reference(new List<IKey>
        {
            new Key(KeyType.Submodel, submodelId),
            new Key(KeyType.SubmodelElementCollection, "CapabilitySet"),
            new Key(KeyType.SubmodelElementCollection, "DockingContainer"),
            new Key(KeyType.Capability, "Docking")
        }) { Type = ReferenceType.ModelReference };

        var composedOf = new CapabilityComposedOfSetDefinition(
            "ComposedOfSet",
            new List<RelationshipElementDefinition>
            {
                new("Composed_Retrieve", parentRef, child1Ref),
                new("Composed_Docking", parentRef, child2Ref)
            });

        var relations = new CapabilityRelationsDefinition(
            "CapabilityRelations",
            new List<RelationshipElementDefinition>(),
            ComposedOfSet: composedOf);

        var parentWithRelations = new CapabilityContainerDefinition(
            parentContainer.IdShort,
            new CapabilityElementDefinition("DockRetrieveHandoverCapability"),
            Relations: relations);

        var fullSet = new CapabilitySetDefinition(
            "CapabilitySet",
            new List<CapabilityContainerDefinition> { parentWithRelations, child1, child2 });

        var template = new CapabilityDescriptionTemplate(submodelId, fullSet, "TestSubmodel");

        var submodel = new CapabilityDescriptionSubmodel(template.Identifier);
        submodel.Apply(template);

        var container = submodel.FindCapabilityContainer("DockRetrieveHandoverContainer");
        Assert.NotNull(container);

        var capContainer = CapabilityContainer.FromSubmodelElement(container!);
        var composed = capContainer.Relations?.ComposedOf;
        Assert.NotNull(composed);

        var children = composed!.Select(rel => rel.Value.Second.Keys?.Last().Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

        Log("Composed children: {0}", string.Join(",", children));

        Assert.Contains("Retrieve", children);
        Assert.Contains("Docking", children);
    }
}
