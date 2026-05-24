using LSLib.LS;
using LSLib.LS.Save;
using System.Text;

namespace LSTools.DivineGUI;

public sealed class VariableDumper : IDisposable
{
    private readonly StreamWriter _writer;
    private Resource? _rsrc;
    private OsirisVariableHelper? _variablesHelper;

    public bool IncludeDeletedVars { get; set; } = false;
    public bool IncludeLocalScopes { get; set; } = false;

    public VariableDumper(Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        _writer = new StreamWriter(outputStream, Encoding.UTF8);
    }

    public void Dispose() => _writer.Dispose();

    private void DumpCharacter(Node characterNode)
    {
        ArgumentNullException.ThrowIfNull(_variablesHelper);

        if (characterNode.Children.TryGetValue("VariableManager", out var varNodes) && varNodes.Count > 0)
        {
            var characterVars = new VariableManager(_variablesHelper);
            characterVars.Load(varNodes[0]);

            string key = characterNode.Attributes.TryGetValue("CurrentTemplate", out var templateAttr) && templateAttr.Value is not null
                ? templateAttr.Value.ToString() ?? string.Empty
                : string.Empty;

            if (characterNode.Attributes.TryGetValue("Stats", out var statsAttr))
            {
                key = $"{key} ({statsAttr.Value})";
            }
            else if (characterNode.Children.TryGetValue("PlayerData", out var playerDataList) && playerDataList.Count > 0)
            {
                var playerDataNode = playerDataList[0];
                if (playerDataNode.Children.TryGetValue("PlayerCustomData", out var customDataList) && customDataList.Count > 0)
                {
                    var customData = customDataList[0];
                    if (customData.Attributes.TryGetValue("Name", out var nameAttr))
                    {
                        key = $"{key} (Player {nameAttr.Value})";
                    }
                }
            }

            DumpVariables(key, characterVars);
        }
    }

    private void DumpItem(Node itemNode)
    {
        ArgumentNullException.ThrowIfNull(_variablesHelper);

        if (itemNode.Children.TryGetValue("VariableManager", out var varNodes) && varNodes.Count > 0)
        {
            var itemVars = new VariableManager(_variablesHelper);
            itemVars.Load(varNodes[0]);

            string key = itemNode.Attributes.TryGetValue("CurrentTemplate", out var templateAttr) && templateAttr.Value is not null
                ? templateAttr.Value.ToString() ?? string.Empty
                : string.Empty;

            if (itemNode.Attributes.TryGetValue("Stats", out var statsAttr))
            {
                key = $"{key} ({statsAttr.Value})";
            }

            DumpVariables(key, itemVars);
        }
    }

    private void DumpGlobals(Node globalVarsNode)
    {
        ArgumentNullException.ThrowIfNull(_variablesHelper);

        var vars = new VariableManager(_variablesHelper);
        vars.Load(globalVarsNode);
        DumpVariables("Globals", vars);
    }

    private void DumpVariables(string label, VariableManager variableMgr)
    {
        var baseVars = variableMgr.GetAll(IncludeDeletedVars);
        var filteredVars = new List<KeyValuePair<string, object>>();

        if (baseVars is System.Collections.IEnumerable enumerableVars)
        {
            var enumerator = enumerableVars.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current is KeyValuePair<string, object> kv)
                    {
                        if (!IncludeLocalScopes && kv.Key.Contains('.'))
                        {
                            continue;
                        }
                        filteredVars.Add(kv);
                    }
                }
            }
            finally
            {
                if (enumerator is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        if (filteredVars.Count > 0)
        {
            _writer.Write($"{label}:\n");
            foreach (var kv in filteredVars)
            {
                _writer.Write($"\t{kv.Key}: {kv.Value}\n");
            }
            _writer.Write("\n");
        }
    }

    public bool Load(Resource resource)
    {
        _rsrc = resource;

        if (!resource.Regions.TryGetValue("OsirisVariableHelper", out var osiHelper) ||
            !osiHelper.Children.ContainsKey("IdentifierTable"))
        {
            return false;
        }

        _variablesHelper = new OsirisVariableHelper();
        _variablesHelper.Load(osiHelper);
        return true;
    }

    public void DumpGlobals()
    {
        ArgumentNullException.ThrowIfNull(_rsrc);

        if (!_rsrc.Regions.TryGetValue("OsirisVariableHelper", out var osiHelper)) return;
        if (!osiHelper.Children.TryGetValue("VariableManager", out var varManagerList) || varManagerList.Count == 0) return;

        _writer.Write(" === DUMP OF GLOBALS === \n");
        DumpGlobals(varManagerList[0]);
    }

    public void DumpCharacters()
    {
        ArgumentNullException.ThrowIfNull(_rsrc);

        _writer.Write("\n === DUMP OF CHARACTERS === \n");

        if (!_rsrc.Regions.TryGetValue("Characters", out var charactersRegion)) return;
        if (!charactersRegion.Children.TryGetValue("CharacterFactory", out var factoryList) || factoryList.Count == 0) return;
        if (!factoryList[0].Children.TryGetValue("Characters", out var containerList) || containerList.Count == 0) return;
        if (!containerList[0].Children.TryGetValue("Character", out var charactersList)) return;

        foreach (var character in charactersList)
        {
            DumpCharacter(character);
        }
    }

    public void DumpItems()
    {
        ArgumentNullException.ThrowIfNull(_rsrc);

        _writer.Write("\n === DUMP OF ITEMS === \n");

        if (!_rsrc.Regions.TryGetValue("Items", out var itemsRegion)) return;
        if (!itemsRegion.Children.TryGetValue("ItemFactory", out var factoryList) || factoryList.Count == 0) return;
        if (!factoryList[0].Children.TryGetValue("Items", out var containerList) || containerList.Count == 0) return;
        if (!containerList[0].Children.TryGetValue("Item", out var itemsList)) return;

        foreach (var item in itemsList)
        {
            DumpItem(item);
        }
    }
}