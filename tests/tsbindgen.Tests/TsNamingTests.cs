using tsbindgen.Config;
using tsbindgen.Snapshot;
using Xunit;

namespace tsbindgen.Tests;

public class TsNamingTests
{
    // Helper to create simple TypeReference
    private static TypeReference SimpleType(string ns, string name) =>
        new TypeReference(ns, name, [], 0, 0, null);

    // Helper to create nested TypeReference
    private static TypeReference NestedType(TypeReference parent, string name) =>
        new TypeReference(null, name, [], 0, 0, parent);

    [Fact]
    public void ForAnalysis_SimpleType_ReturnsIdentifier()
    {
        var typeRef = new TypeReference(
            Namespace: "System",
            TypeName: "Console",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var result = TsNaming.ForAnalysis(typeRef);

        Assert.Equal("Console", result);
    }

    [Fact]
    public void ForAnalysis_GenericType_PreservesArityInName()
    {
        var typeRef = new TypeReference(
            Namespace: "System.Collections.Generic",
            TypeName: "List_1",  // TypeName already has arity suffix
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var result = TsNaming.ForAnalysis(typeRef);

        Assert.Equal("List_1", result);
    }

    [Fact]
    public void ForAnalysis_NestedType_JoinsWithUnderscore()
    {
        var parentType = new TypeReference(
            Namespace: "System",
            TypeName: "Console",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var nestedType = new TypeReference(
            Namespace: null,  // Nested types don't repeat namespace
            TypeName: "Error",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: parentType);

        var result = TsNaming.ForAnalysis(nestedType);

        Assert.Equal("Console_Error", result);
    }

    [Fact]
    public void ForAnalysis_NestedGenericType_CombinesUnderscores()
    {
        var parentType = new TypeReference(
            Namespace: "System",
            TypeName: "Console",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var nestedType = new TypeReference(
            Namespace: null,
            TypeName: "Error_1",  // Arity in TypeName
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: parentType);

        var result = TsNaming.ForAnalysis(nestedType);

        Assert.Equal("Console_Error_1", result);
    }

    [Fact]
    public void ForAnalysis_TypeWithUnderscores_PreservesUnderscores()
    {
        var typeRef = new TypeReference(
            Namespace: "System.Runtime.InteropServices",
            TypeName: "BIND_OPTS",  // Literal underscores in name
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var result = TsNaming.ForAnalysis(typeRef);

        Assert.Equal("BIND_OPTS", result);
    }

    [Fact]
    public void ForAnalysis_MultipleNestingLevels_JoinsWithUnderscores()
    {
        var topLevel = new TypeReference(
            Namespace: "System",
            TypeName: "Outer_1",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var nested = new TypeReference(
            Namespace: null,
            TypeName: "Inner_2",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: topLevel);

        var result = TsNaming.ForAnalysis(nested);

        Assert.Equal("Outer_1_Inner_2", result);
    }

    [Fact]
    public void ForEmit_SimpleType_ReturnsIdentifier()
    {
        var typeRef = new TypeReference(
            Namespace: "System",
            TypeName: "Console",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var result = TsNaming.ForEmit(typeRef);

        Assert.Equal("Console", result);
    }

    [Fact]
    public void ForEmit_GenericType_PreservesArityInName()
    {
        var typeRef = new TypeReference(
            Namespace: "System.Collections.Generic",
            TypeName: "List_1",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var result = TsNaming.ForEmit(typeRef);

        Assert.Equal("List_1", result);
    }

    [Fact]
    public void ForEmit_NestedType_JoinsWithDollar()
    {
        var parentType = new TypeReference(
            Namespace: "System",
            TypeName: "Console",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var nestedType = new TypeReference(
            Namespace: null,
            TypeName: "Error",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: parentType);

        var result = TsNaming.ForEmit(nestedType);

        Assert.Equal("Console$Error", result);
    }

    [Fact]
    public void ForEmit_NestedGenericType_CombinesDollarAndUnderscore()
    {
        var parentType = new TypeReference(
            Namespace: "System",
            TypeName: "Console",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var nestedType = new TypeReference(
            Namespace: null,
            TypeName: "Error_1",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: parentType);

        var result = TsNaming.ForEmit(nestedType);

        Assert.Equal("Console$Error_1", result);
    }

    [Fact]
    public void ForEmit_TypeWithUnderscores_PreservesUnderscores()
    {
        var typeRef = new TypeReference(
            Namespace: "System.Runtime.InteropServices",
            TypeName: "BIND_OPTS",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var result = TsNaming.ForEmit(typeRef);

        Assert.Equal("BIND_OPTS", result);
    }

    [Fact]
    public void ForEmit_MultipleNestingLevels_JoinsWithDollars()
    {
        var topLevel = new TypeReference(
            Namespace: "System",
            TypeName: "Outer_1",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var nested = new TypeReference(
            Namespace: null,
            TypeName: "Inner_2",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: topLevel);

        var result = TsNaming.ForEmit(nested);

        Assert.Equal("Outer_1$Inner_2", result);
    }

    [Fact]
    public void ForEmit_DeepNesting_HandlesCorrectly()
    {
        var level1 = new TypeReference(
            Namespace: "System.Runtime.Intrinsics.X86",
            TypeName: "Avx10v1",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var level2 = new TypeReference(
            Namespace: null,
            TypeName: "V512",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: level1);

        var level3 = new TypeReference(
            Namespace: null,
            TypeName: "X64",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: level2);

        var result = TsNaming.ForEmit(level3);

        Assert.Equal("Avx10v1$V512$X64", result);
    }

    [Fact]
    public void Phase3AndPhase4_DifferOnlyInNestingSeparator()
    {
        var parentType = new TypeReference(
            Namespace: "System",
            TypeName: "Console",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: null);

        var nestedType = new TypeReference(
            Namespace: null,
            TypeName: "Error_1",
            GenericArgs: [],
            ArrayRank: 0,
            PointerDepth: 0,
            DeclaringType: parentType);

        var forAnalysis = TsNaming.ForAnalysis(nestedType);
        var forEmit = TsNaming.ForEmit(nestedType);

        Assert.Equal("Console_Error_1", forAnalysis);
        Assert.Equal("Console$Error_1", forEmit);
    }
}
