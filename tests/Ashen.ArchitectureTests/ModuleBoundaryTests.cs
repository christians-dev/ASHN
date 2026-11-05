using System.Reflection;

using Ashen.BuildingBlocks.Web;

namespace Ashen.ArchitectureTests;

/// <summary>Constitution I and II, enforced: modules only see each other's Contracts.</summary>
public class ModuleBoundaryTests
{
    private const string BuildingBlocks = "Ashen.BuildingBlocks";

    private static readonly Assembly[] AshenAssemblies = Directory
        .GetFiles(AppContext.BaseDirectory, "Ashen.*.dll")
        .Select(path => Assembly.LoadFrom(path))
        .ToArray();

    private static IEnumerable<Assembly> Contracts => AshenAssemblies.Where(a => a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal));

    private static IEnumerable<Assembly> Modules => AshenAssemblies.Where(a =>
        Contracts.Any(c => c.GetName().Name == a.GetName().Name + ".Contracts"));

    public static TheoryData<string> ModuleNames => new(Modules.Select(m => m.GetName().Name!));
    public static TheoryData<string> ContractNames => new(Contracts.Select(c => c.GetName().Name!));

    [Fact]
    public void All_eight_modules_are_found()
    {
        Assert.Equal(8, Modules.Count());
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_references_only_building_blocks_and_contracts(string module)
    {
        var forbidden = AshenReferences(module)
            .Where(name => name != BuildingBlocks && !name.EndsWith(".Contracts", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Theory]
    [MemberData(nameof(ContractNames))]
    public void Contracts_reference_only_building_blocks(string contracts)
    {
        Assert.All(AshenReferences(contracts), name => Assert.Equal(BuildingBlocks, name));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_exposes_only_its_entry_point(string module)
    {
        // EF Core generates migrations as public classes; everything else is internal.
        var unexpected = Load(module).GetExportedTypes()
            .Where(t => !typeof(IModule).IsAssignableFrom(t))
            .Where(t => t.Namespace?.EndsWith(".Migrations", StringComparison.Ordinal) != true)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(unexpected);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_has_no_technical_layer_folders(string module)
    {
        string[] layers = ["Controllers", "Services", "Repositories"];

        var offenders = Load(module).GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.Split('.').Any(layers.Contains))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static Assembly Load(string name) => AshenAssemblies.Single(a => a.GetName().Name == name);

    private static IEnumerable<string> AshenReferences(string name) =>
        Load(name).GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith("Ashen.", StringComparison.Ordinal));
}
