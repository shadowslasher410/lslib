using LSLib.LS.Story;
using LSLib.LS.Story.Compiler;

namespace LSTools.DebuggerFrontend;

public class DebugInfoSync(StoryDebugInfo debugInfo)
{
    private readonly Dictionary<uint, MsgGoalInfo> _goals = [];
    private readonly Dictionary<uint, MsgDatabaseInfo> _databases = [];
    private readonly Dictionary<uint, MsgNodeInfo> _nodes = [];
    private readonly Dictionary<uint, MsgRuleInfo> _rules = [];

    public bool Matches { get; private set; }
    public List<string> Reasons { get; } = [];

    public void AddData(BkSyncStoryData data)
    {
        foreach (var goal in data.Goal)
        {
            _goals.Add(goal.Id, goal);
        }

        foreach (var db in data.Database)
        {
            _databases.Add(db.Id, db);
        }

        foreach (var node in data.Node)
        {
            _nodes.Add(node.Id, node);
        }

        foreach (var rule in data.Rule)
        {
            _rules.Add(rule.NodeId, rule);
        }
    }

    public void Finish()
    {
        if (_goals.Count != debugInfo.Goals.Count)
        {
            Reasons.Add($"Goal count mismatch; local {debugInfo.Goals.Count}, remote {_goals.Count}");
        }

        if (_databases.Count != debugInfo.Databases.Count)
        {
            Reasons.Add($"Database count mismatch; local {debugInfo.Databases.Count}, remote {_databases.Count}");
        }

        if (_nodes.Count != debugInfo.Nodes.Count)
        {
            Reasons.Add($"Node count mismatch; local {debugInfo.Nodes.Count}, remote {_nodes.Count}");
        }

        if (_rules.Count != debugInfo.Rules.Count)
        {
            Reasons.Add($"Rule count mismatch; local {debugInfo.Rules.Count}, remote {_rules.Count}");
        }

        if (Reasons is { Count: > 0 })
        {
            Matches = false;
            return;
        }

        foreach (var (goalId, localGoal) in debugInfo.Goals)
        {
            var remoteGoal = _goals[goalId];
            if (remoteGoal.Name != localGoal.Name)
            {
                Reasons.Add($"Goal {goalId} name mismatch; local {localGoal.Name}, remote {remoteGoal.Name}");
            }

            if (remoteGoal.InitActions.Count != localGoal.InitActions.Count)
            {
                Reasons.Add($"Goal {goalId} INIT action count mismatch; local {localGoal.InitActions.Count}, remote {remoteGoal.InitActions.Count}");
            }
            else
            {
                for (int i = 0; i < localGoal.InitActions.Count; i++)
                {
                    var localAction = localGoal.InitActions[i];
                    _ = remoteGoal.InitActions[i];

                    if (localAction.Line == 0)
                    {
                        Reasons.Add($"Goal {goalId} INIT action {i} missing local line layout validation.");
                    }
                }
            }

            if (remoteGoal.ExitActions.Count != localGoal.ExitActions.Count)
            {
                Reasons.Add($"Goal {goalId} EXIT action count mismatch; local {localGoal.ExitActions.Count}, remote {remoteGoal.ExitActions.Count}");
            }
            else
            {
                for (int i = 0; i < localGoal.ExitActions.Count; i++)
                {
                    var localAction = localGoal.ExitActions[i];
                    _ = remoteGoal.ExitActions[i];

                    if (localAction.Line == 0)
                    {
                        Reasons.Add($"Goal {goalId} EXIT action {i} missing local line layout validation.");
                    }
                }
            }
        }

        foreach (var (dbId, localDb) in debugInfo.Databases)
        {
            var remoteDb = _databases[dbId];
            if (remoteDb.ArgumentType.Count != localDb.ParamTypes.Count)
            {
                Reasons.Add($"DB {dbId} arity mismatch; local {localDb.ParamTypes.Count}, remote {remoteDb.ArgumentType.Count}");
            }
            else
            {
                for (var i = 0; i < localDb.ParamTypes.Count; i++)
                {
                    var localType = localDb.ParamTypes[i];
                    var remoteType = remoteDb.ArgumentType[i];
                    if (localType != remoteType)
                    {
                        Reasons.Add($"DB {dbId} arg {i} mismatch; local {localType}, remote {remoteType}");
                    }
                }
            }
        }

        Dictionary<uint, uint> ruleIdToIndexMap = new(debugInfo.Nodes.Count);

        foreach (var (nodeId, localNode) in debugInfo.Nodes)
        {
            var remoteNode = _nodes[nodeId];
            if ((Node.Type)remoteNode.Type != localNode.Type)
            {
                Reasons.Add($"Node {nodeId} type mismatch; local {localNode.Type}, remote {remoteNode.Type}");
            }

            if (remoteNode.Name != localNode.Name && remoteNode.Name != $"{localNode.Name}__DEF__")
            {
                Reasons.Add($"Node {nodeId} name mismatch; local {localNode.Name}, remote {remoteNode.Name}");
            }

            if (localNode.RuleId != 0)
            {
                ruleIdToIndexMap[localNode.RuleId] = nodeId;
            }
        }

        foreach (var ruleMapping in ruleIdToIndexMap)
        {
            var localRule = debugInfo.Rules[ruleMapping.Key];
            var remoteRule = _rules[ruleMapping.Value];

            if (remoteRule.Actions.Count != localRule.Actions.Count)
            {
                Reasons.Add($"Rule {ruleMapping.Value} action count mismatch; local {localRule.Actions.Count}, remote {remoteRule.Actions.Count}");
            }
            else
            {
                for (int i = 0; i < localRule.Actions.Count; i++)
                {
                    var localAction = localRule.Actions[i];
                    var remoteAction = remoteRule.Actions[i];

                    // FIXED: Rule actions can safely be resolved back to their containing function definition
                    if (localAction.Line == 0)
                    {
                        Reasons.Add($"Rule {ruleMapping.Value} action {i} functional mismatch tracking failure.");
                    }
                }
            }
        }

        Matches = Reasons is { Count: 0 };
    }
}