using System;
using System.Collections.Generic;
using AdvancedControllerProcessor.Helpers;
using Xunit;

namespace AdvancedControllerProcessor.Tests.Helpers;

public sealed class InputThreadOptimizerTests
{
    [Fact]
    public void HybridCpu_KeepsOnlyPerformanceCoreThreads()
    {
        // i5-13400F-style layout: 6 performance cores (class 1) each with
        // hyper-threading (2 logical), followed by 4 efficiency cores
        // (class 0). Cores are enumerated sequentially, so with
        // logicalPerCore = 2 the P-threads occupy bits 0..11.
        var classes = new List<int> { 1, 1, 1, 1, 1, 1, 0, 0, 0, 0 };

        long mask = InputThreadOptimizer.BuildAffinityMask(classes, 2);

        Assert.Equal((1L << 12) - 1, mask);
    }

    [Fact]
    public void UniformCpu_ReturnsZero_KeepCurrentAffinity()
    {
        var classes = new List<int> { 0, 0, 0, 0 };

        Assert.Equal(0, InputThreadOptimizer.BuildAffinityMask(classes, 2));
    }

    [Fact]
    public void EmptyInput_ReturnsZero()
    {
        Assert.Equal(0, InputThreadOptimizer.BuildAffinityMask(Array.Empty<int>(), 1));
    }
}
