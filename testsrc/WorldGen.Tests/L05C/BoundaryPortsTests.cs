using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Hydrology.Boundaries;

namespace ISRWorldGen.Tests.L05C;

[TestClass]
public sealed class BoundaryPortsTests
{
    private static readonly StableId Relief = new(7, 11);

    [TestMethod]
    public void T05_05_FourRegionRiverPublishesExactlyTheSamePortsInBothExplorationOrders()
    {
        BoundaryPortRequest[] source =
        [
            Request(10, 0, 100, 101, 10, 0, 100, 200, 9, 2, 3, 4d),
            Request(10, 1, 101, 102, 20, 0, 100, 200, 8, 3, 4, 7d),
            Request(10, 2, 102, 103, 30, 0, 100, 200, 7, 4, 5, 11d),
        ];

        BoundaryPortSnapshot sourceToSea = BoundaryPortPublisher.Publish(3, Relief, source);
        BoundaryPortSnapshot seaToSource = BoundaryPortPublisher.Publish(3, Relief, source.Reverse());

        Assert.HasCount(3, sourceToSea.Ports, "The fixture crosses four technical regions through three shared faces.");
        CollectionAssert.AreEqual(sourceToSea.Ports.ToArray(), seaToSource.Ports.ToArray());
        foreach (BoundaryPort port in sourceToSea.Ports)
        {
            Assert.AreEqual(3, port.Iteration);
            Assert.AreEqual(Relief, port.ReliefSignature);
            Assert.AreEqual(StableId.Derive(RandomDomain.Hydrology, port.OwnerId, port.OwnerLocalIndex), port.PortId);
        }
    }

    [TestMethod]
    public void T05_06_ThreeRefinementsAndReloadsConserveBudgetWithoutMutatingPublishedRiver()
    {
        BoundaryPortSnapshot parent = BoundaryPortPublisher.Publish(8, Relief,
        [
            Request(70, 0, 200, 201, 50, 50, 50, 50, 20, 6, 7, 12d, 8),
        ]);
        BoundaryPort published = parent.Ports.Single();
        BoundaryRefinementSnapshot first = BoundaryRefinementPlanner.Refine(parent,
        [Child(published, 71, 0, 49, 50, 3d), Child(published, 72, 0, 50, 50, 4d)]);
        BoundaryRefinementSnapshot reloaded = BoundaryRefinementPlanner.Refine(parent, first.Children.Reverse());
        BoundaryRefinementSnapshot third = BoundaryRefinementPlanner.Refine(parent,
        [Child(published, 73, 0, 48, 50, 2d), Child(published, 74, 0, 50, 49, 5d)]);

        CollectionAssert.AreEqual(first.Children.ToArray(), reloaded.Children.ToArray());
        Assert.AreEqual(published, parent.Ports.Single(), "Refinement must retain the original immutable port object/value.");
        AssertConserved(first, published, 7d);
        AssertConserved(reloaded, published, 7d);
        AssertConserved(third, published, 7d);
    }

    [TestMethod]
    public void BoundaryPorts_RejectDuplicateIdentityBadCorridorExcessBudgetMixedProvenanceAndNonFiniteFlow()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BoundaryPortPublisher.Publish(1, Relief,
        [Request(1, 0, 1, 2, 0, 0, 1, 1, 2, 1, 1, 1d, 1), Request(1, 0, 1, 2, 1, 0, 1, 1, 2, 1, 1, 1d, 1)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BoundaryPortPublisher.Publish(1, Relief,
        [Request(1, 0, 1, 2, 0, 0, 1, 1, 2, 1, 1, double.PositiveInfinity, 1)]));

        BoundaryPortSnapshot parent = BoundaryPortPublisher.Publish(1, Relief, [Request(9, 0, 1, 2, 0, 0, 1, 1, 2, 1, 1, 5d, 1)]);
        BoundaryPort port = parent.Ports.Single();
        Assert.ThrowsExactly<ArgumentException>(() => BoundaryRefinementPlanner.Refine(parent, [Child(port, 10, 0, 2, 0, 1d)]));
        Assert.ThrowsExactly<ArgumentException>(() => BoundaryRefinementPlanner.Refine(parent, [Child(port, 10, 0, 0, 0, 6d)]));
        BoundaryPort incompatible = Child(port, 10, 0, 0, 0, 1d).ChildPort with { Iteration = 2 };
        Assert.ThrowsExactly<ArgumentException>(() => BoundaryRefinementPlanner.Refine(parent, [new BoundaryRefinementChild(port.PortId, incompatible)]));
        BoundaryPort foreignRelief = Child(port, 10, 0, 0, 0, 1d).ChildPort with { ReliefSignature = new StableId(9, 9) };
        Assert.ThrowsExactly<ArgumentException>(() => BoundaryRefinementPlanner.Refine(parent, [new BoundaryRefinementChild(port.PortId, foreignRelief)]));
    }

    [TestMethod]
    public void BoundaryRefinement_RejectsOverflowAndDoesNotUseInputEnumerationAsAnOracle()
    {
        BoundaryPortSnapshot parent = BoundaryPortPublisher.Publish(5, Relief, [Request(9, 0, 1, 2, 0, 0, 2, 2, 4, 1, 1, double.MaxValue, 5)]);
        BoundaryPort port = parent.Ports.Single();
        BoundaryRefinementChild one = Child(port, 10, 0, 0, 0, double.MaxValue);
        BoundaryRefinementChild two = Child(port, 11, 0, 1, 1, double.MaxValue);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BoundaryRefinementPlanner.Refine(parent, [one, two]));
    }

    [TestMethod]
    public void BoundaryRefinement_CanonicalizesFiniteHeterogeneousReductionsBeforeEveryAllocationDecision()
    {
        BoundaryPortSnapshot parent = BoundaryPortPublisher.Publish(6, Relief, [Request(80, 0, 300, 301, 20, 20, 20, 20, 30, 4, 4, 1000d, 6)]);
        BoundaryPort port = parent.Ports.Single();
        BoundaryRefinementChild[] supplied =
        [
            Child(port, 83, 0, 3, 3, 700.1d), Child(port, 81, 0, 1, 1, 0.2d), Child(port, 82, 0, 2, 2, 0.3d),
        ];

        BoundaryRefinementSnapshot expected = BoundaryRefinementPlanner.Refine(parent, supplied);
        foreach (BoundaryRefinementChild[] permutation in Permutations(supplied))
        {
            BoundaryRefinementSnapshot actual = BoundaryRefinementPlanner.Refine(parent, permutation);
            CollectionAssert.AreEqual(expected.Children.ToArray(), actual.Children.ToArray());
            CollectionAssert.AreEqual(expected.Allocations.ToArray(), actual.Allocations.ToArray(),
                "Canonical child ordering must make child-flow, retained-flow, and acceptance decision bit-identical across permutations.");
        }
    }

    [TestMethod]
    public void BoundaryRefinement_RejectsFalsifiedPublicIdentityAndInvalidPublicProfile()
    {
        BoundaryPortSnapshot parent = BoundaryPortPublisher.Publish(4, Relief, [Request(90, 0, 400, 401, 5, 5, 5, 5, 10, 2, 2, 8d, 4)]);
        BoundaryPort port = parent.Ports.Single();
        BoundaryPort valid = Child(port, 91, 0, 4, 4, 2d).ChildPort;
        BoundaryPort falsifiedId = valid with { PortId = StableId.Zero };
        BoundaryPort invalidProfile = valid with { Profile = new BoundaryChannelProfile(valid.WaterLevelQuantized + 1, 2, 2) };

        Assert.ThrowsExactly<ArgumentException>(() => BoundaryRefinementPlanner.Refine(parent, [new BoundaryRefinementChild(port.PortId, falsifiedId)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BoundaryRefinementPlanner.Refine(parent, [new BoundaryRefinementChild(port.PortId, invalidProfile)]));
    }

    private static void AssertConserved(BoundaryRefinementSnapshot result, BoundaryPort parent, double expectedChildren)
    {
        BoundaryRefinementAllocation allocation = result.Allocations.Single();
        Assert.AreEqual(parent.PortId, allocation.ParentPortId);
        Assert.AreEqual(expectedChildren, allocation.ChildFlowModelVolumePerYear, 1e-12);
        Assert.AreEqual(parent.ReferenceFlowModelVolumePerYear, allocation.ChildFlowModelVolumePerYear + allocation.RetainedParentFlowModelVolumePerYear, 1e-12);
    }

    private static BoundaryPortRequest Request(ulong owner, ulong index, long firstRegion, long secondRegion, long x, long z, long maxX, long maxZ, long water, int width, int depth, double flow, int iteration = 3) =>
        new(new StableId(0, owner), index, new BoundaryPlane(firstRegion, secondRegion, BoundaryAxis.EastWest), new BoundaryCrossing(x, z), water,
            new BoundaryChannelProfile(water - 1, width, depth), flow, new BoundaryCorridor(0, maxX, 0, maxZ), iteration, Relief);

    private static BoundaryRefinementChild Child(BoundaryPort parent, ulong owner, ulong index, long x, long z, double flow) =>
        new(parent.PortId, new BoundaryPort(StableId.Derive(RandomDomain.Hydrology, new StableId(0, owner), index), new StableId(0, owner), index,
            parent.Plane, new BoundaryCrossing(x, z), parent.WaterLevelQuantized, parent.Profile, flow, parent.ProtectedCorridor, parent.Iteration, parent.ReliefSignature));

    private static IEnumerable<BoundaryRefinementChild[]> Permutations(BoundaryRefinementChild[] values)
    {
        for (int first = 0; first < values.Length; first++)
        for (int second = 0; second < values.Length; second++)
        for (int third = 0; third < values.Length; third++)
            if (first != second && first != third && second != third)
                yield return [values[first], values[second], values[third]];
    }
}
