using System.Diagnostics;
using System.Globalization;

namespace LSLib.LS.Story.Compiler;

public enum RelOpType { Less, LessOrEqual, Greater, GreaterOrEqual, Equal, NotEqual }
public class Compiler
{
    public CompilationContext Context
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value), "Compiler context cannot be null.");
    } = default!;
    public HashSet<FunctionNameAndArity> IgnoreUnusedDatabases { get; set; } = [];
    public TargetGame Game { get; set; } = TargetGame.DOS2;
    public bool AllowTypeCoercion { get; set; }
    public HashSet<string> TypeCoercionWhitelist { get; set; } = [];

    private string TypeToName(uint typeId)
    {
        return Context.TypesById.TryGetValue(typeId, out var type) ? type.Name : $"UNKNOWN_TYPE_{typeId}";
    }

    private string TypeToName(Value.Type typeId)
    {
        return TypeToName((uint)typeId);
    }

    private void VerifyParamCompatibility(FunctionSignature func, int paramIndex, FunctionParam param, IRValue value)
    {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(param);
        ArgumentNullException.ThrowIfNull(value);

        if (param.Type.IntrinsicTypeId != value.Type.IntrinsicTypeId)
        {
            // BG3 allows promoting integer constants to float
            if (Game == TargetGame.BG3 && value is IRConstant
                && (param.Type.IntrinsicTypeId == Value.Type.Float || param.Type.IntrinsicTypeId == Value.Type.Integer64)
                && value.Type.IntrinsicTypeId == Value.Type.Integer)
            {
                return;
            }
            string paramIdentifier = !string.IsNullOrEmpty(param.Name) ? param.Name : paramIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

            Context.Log.Error(value.Location,
                DiagnosticCode.LocalTypeMismatch,
                "Parameter {0} of {1} \"{2}\" expects {3}; {4} specified",
                paramIdentifier, func.Type, func.Name, param.Type.Name, value.Type.Name);
            return;
        }

        if (IsGuidAliasToAliasCast(param.Type, value.Type))
        {
            string paramIdentifier = !string.IsNullOrEmpty(param.Name) ? param.Name : paramIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

            Context.Log.Error(value.Location,
                DiagnosticCode.GuidAliasMismatch,
                "Parameter {0} of {1} \"{2}\" has GUID type {3}; {4} specified",
                paramIdentifier, func.Type, func.Name, param.Type.Name, value.Type.Name);
            return;
        }
    }

    private void VerifyIRFact(IRFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Database is not { Name: { } dbNameObj }) return;

        var db = Context.LookupSignature(new(dbNameObj.Name, fact.Elements.Count));
        if (db is null)
        {
            Context.Log.Error(fact.Location,
                DiagnosticCode.UnresolvedSymbol,
                $"Database \"{dbNameObj.Name}\" could not be resolved");
            return;
        }

        if (db.Type is not (FunctionType.Database or FunctionType.Call or FunctionType.SysCall or FunctionType.Proc))
        {
            Context.Log.Error(fact.Location,
                DiagnosticCode.InvalidSymbolInFact,
                $"Init/Exit actions can only reference databases, calls and PROCs; \"{dbNameObj.Name}\" is a {db.Type}");
            return;
        }

        if (fact.Not) db.Deleted = true;
        else db.Inserted = true;

        int index = 0;
        foreach (var param in db.Params)
        {
            if (index >= fact.Elements.Count) break;
            var ele = fact.Elements[index];
            index++;

            if (ele.Type is null)
            {
                Context.Log.Error(ele.Location,
                    DiagnosticCode.InternalError,
                    "No type information available for fact argument");
                continue;
            }

            VerifyParamCompatibility(db, index, param, ele);
        }
    }

    private void VerifyIRStatement(IRRule rule, IRStatement statement)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(statement);

        if (statement.Func is not { Name: { } funcNameObj }) return;

        var func = Context.LookupSignature(new(funcNameObj.Name, statement.Params.Count));
        if (func is null)
        {
            Context.Log.Error(statement.Location,
                DiagnosticCode.UnresolvedSymbol,
                $"Symbol \"{funcNameObj.Name}\" could not be resolved");
            return;
        }

        if (!func.FullyTyped)
        {
            Context.Log.Error(statement.Location,
                DiagnosticCode.UnresolvedSignature,
                $"Signature of \"{funcNameObj.Name}\" could not be determined");
            return;
        }

        if (func.Type is not (FunctionType.Database or FunctionType.Call or FunctionType.SysCall or FunctionType.Proc))
        {
            Context.Log.Error(statement.Location,
                DiagnosticCode.InvalidSymbolInStatement,
                $"KB rule actions can only reference databases, calls and PROCs; \"{funcNameObj.Name}\" is a {func.Type}");
            return;
        }

        if (statement.Not && func.Type != FunctionType.Database)
        {
            Context.Log.Error(statement.Location,
                DiagnosticCode.CanOnlyDeleteFromDatabase,
                $"KB rule NOT actions can only reference databases; \"{funcNameObj.Name}\" is a {func.Type}");
            return;
        }

        if (statement.Not) func.Deleted = true;
        else func.Inserted = true;

        int index = 0;
        foreach (var param in func.Params)
        {
            if (index >= statement.Params.Count) break;
            var ele = statement.Params[index];

            ValueType type = ele.Type;
            if (type is null)
            {
                Context.Log.Error(ele.Location,
                    DiagnosticCode.InternalError,
                    "No type information available for statement argument");
                continue;
            }

            VerifyIRValue(rule, ele, func);
            VerifyIRValueCall(rule, ele, func, index, -1, statement.Not);
            VerifyParamCompatibility(func, index, param, ele);

            index++;
        }
    }

    private void VerifyIRVariable(IRRule rule, IRVariable variable, FunctionSignature func)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(variable);

        if (variable.Index >= rule.Variables.Count) return;
        var ruleVar = rule.Variables[variable.Index];

        if (variable.Type is null)
        {
            Context.Log.Error(variable.Location,
                DiagnosticCode.UnresolvedType,
                "Type of variable {0} could not be determined",
                ruleVar.Name);
            return;
        }

        if (ruleVar.Type is null)
        {
            Context.Log.Error(variable.Location,
                DiagnosticCode.UnresolvedType,
                "Type of rule variable {0} could not be determined",
                ruleVar.Name);
            return;
        }

        if ((func is null || TypeCoercionWhitelist is null || !TypeCoercionWhitelist.Contains(func.GetNameAndArity().ToString()))
            && !AllowTypeCoercion)
        {
            if (!AreIntrinsicTypesCompatible(ruleVar.Type.IntrinsicTypeId, variable.Type.IntrinsicTypeId))
            {
                Context.Log.Error(variable.Location,
                    DiagnosticCode.CastToUnrelatedType,
                    "Cannot cast {1} variable {0} to unrelated type {2}",
                    ruleVar.Name, ruleVar.Type.Name, variable.Type.Name);
                return;
            }

            if (IsRiskyComparison(ruleVar.Type.IntrinsicTypeId, variable.Type.IntrinsicTypeId))
            {
                Context.Log.Error(variable.Location,
                    DiagnosticCode.RiskyComparison,
                    "Coercion of {1} variable {0} to {2} may trigger incorrect behavior",
                    ruleVar.Name, ruleVar.Type.Name, variable.Type.Name);
                return;
            }

            if (IsGuidAliasToAliasCast(ruleVar.Type, variable.Type))
            {
                Context.Log.Error(variable.Location,
                    DiagnosticCode.CastToUnrelatedGuidAlias,
                    "{1} variable {0} converted to unrelated type {2}",
                    ruleVar.Name, ruleVar.Type.Name, variable.Type.Name);
            }
        }
    }

    private void VerifyIRConstant(IRConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        if (constant.Type.IntrinsicTypeId == Value.Type.GuidString)
        {
            var nameWithoutType = constant.StringValue;
            int underscore = constant.StringValue.IndexOf('_');
            if (underscore != -1)
            {
                var prefix = constant.StringValue[..underscore];
                ValueType? type = Context.LookupType(prefix);
                if (type is not null)
                {
                    nameWithoutType = constant.StringValue[(underscore + 1)..];
                    if (constant.Type.TypeId > CompilationContext.MaxIntrinsicTypeId
                        && type.TypeId != constant.Type.TypeId)
                    {
                        Context.Log.Error(constant.Location,
                            DiagnosticCode.GuidAliasMismatch,
                            "GUID constant \"{0}\" has inferred type {1}",
                            constant.StringValue, constant.Type.Name);
                    }
                }
                else if (prefix.Contains("GUID", StringComparison.OrdinalIgnoreCase) && Game != TargetGame.BG3)
                {
                    Context.Log.Warn(constant.Location,
                        DiagnosticCode.GuidPrefixNotKnown,
                        "GUID constant \"{0}\" is prefixed with unknown type {1}",
                        constant.StringValue, prefix);
                }
            }


            if (constant.StringValue.Length >= 36)
            {
                var guid = constant.StringValue[^36..];
                if (!Context.GameObjects.TryGetValue(guid, out GameObjectInfo? objectInfo))
                {
                    Context.Log.Warn(constant.Location,
                        DiagnosticCode.UnresolvedGameObjectName,
                        "Object \"{0}\" could not be resolved",
                        constant.StringValue);
                }
                else
                {
                    if (objectInfo.Name != nameWithoutType)
                    {
                        Context.Log.Warn(constant.Location,
                            DiagnosticCode.GameObjectNameMismatch,
                            "Constant \"{0}\" references game object with different name (\"{1}\")",
                            nameWithoutType, objectInfo.Name);
                    }

                    if (constant.Type.TypeId != (uint)Value.Type.GuidString
                        && objectInfo.Type.TypeId != (uint)Value.Type.GuidString
                        && constant.Type.TypeId != objectInfo.Type.TypeId)
                    {
                        Context.Log.Warn(constant.Location,
                            DiagnosticCode.GameObjectTypeMismatch,
                            "Constant \"{0}\" of type {1} references game object of type {2}",
                            constant.StringValue, constant.Type.Name, objectInfo.Type.Name);
                    }
                }
            }
        }
    }

    private void VerifyIRValue(IRRule rule, IRValue value, FunctionSignature func)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(value);
        if (value is IRConstant constant)
        {
            VerifyIRConstant(constant);
        }
        else if (value is IRVariable variable)
        {
            VerifyIRVariable(rule, variable, func);
        }
    }

    private void VerifyIRVariableCall(IRRule rule, IRVariable variable, FunctionSignature signature, int parameterIndex,
        int conditionIndex, bool not)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(signature);

        if (variable.Index >= rule.Variables.Count || parameterIndex >= signature.Params.Count) return;

        var ruleVar = rule.Variables[variable.Index];
        var param = signature.Params[parameterIndex];

        if (param.Direction == ParamDirection.Out && !not)
        {
            Debug.Assert(conditionIndex != -1);
            if (ruleVar.FirstBindingIndex == -1)
            {
                ruleVar.FirstBindingIndex = conditionIndex;
            }
        }
        else if (
            conditionIndex == -1
            || (!ruleVar.IsUnused() && not)
            || (
               signature.Type != FunctionType.Database
                && signature.Type != FunctionType.Event
                && !(rule.GetType().GetProperty("Type")?.GetValue(rule)?.ToString() == "Proc" && conditionIndex == 0 && signature.Type == FunctionType.Proc)
                && !(rule.GetType().GetProperty("Type")?.GetValue(rule)?.ToString() == "Query" && conditionIndex == 0 && signature.Type == FunctionType.UserQuery)
                && param.Direction != ParamDirection.Out
            )
        ) {

            if (
                ruleVar.FirstBindingIndex == -1
                || (conditionIndex != -1 && ruleVar.FirstBindingIndex >= conditionIndex)
            ) {
                string paramName = !string.IsNullOrEmpty(param.Name) ? param.Name : (parameterIndex + 1).ToString(CultureInfo.InvariantCulture);
                if (!ruleVar.IsUnused())
                {
                    Context.Log.Error(variable.Location,
                        DiagnosticCode.ParamNotBound,
                        "Variable {0} is not bound here (when used as parameter {1} of {2} \"{3}\")",
                        ruleVar.Name, paramName, signature.Type, signature.GetNameAndArity());
                }
                else
                {
                    Context.Log.Error(variable.Location,
                        DiagnosticCode.ParamNotBound,
                        "Parameter {0} of {1} \"{2}\" requires a variable or constant, not a placeholder",
                        paramName, signature.Type, signature.GetNameAndArity());
                }
            }
        }
        else
        {
            if (conditionIndex != -1 && ruleVar.FirstBindingIndex == -1 && !not)
            {
                ruleVar.FirstBindingIndex = conditionIndex;
            }
        }
    }

    private void VerifyIRValueCall(IRRule rule, IRValue value, FunctionSignature signature, int parameterIndex,
        int conditionIndex, bool not)
    {
        if (value is IRVariable variable)
        {
            VerifyIRVariableCall(rule, variable, signature, parameterIndex, conditionIndex, not);
        }
    }

    private void VerifyIRFuncCondition(IRRule rule, IRFuncCondition condition, int conditionIndex)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(condition);

        if (condition.Func is not { Name: { } funcNameObj }) return;

        var func = Context.LookupSignature(new(funcNameObj.Name, condition.Params.Count));
        if (func is null)
        {
            Context.Log.Error(condition.Location,
                DiagnosticCode.UnresolvedSymbol,
                $"Symbol \"{funcNameObj.Name}\" could not be resolved");
            return;
        }

        if (!func.FullyTyped)
        {
            Context.Log.Error(condition.Location,
                DiagnosticCode.UnresolvedSignature,
                $"Signature of \"{funcNameObj.Name}\" could not be determined");
            return;
        }

        func.Read = true;

        if (conditionIndex == 0)
        {
            switch (rule.Type)
            {
                case RuleType.Proc:
                    if (func.Type != FunctionType.Proc)
                    {
                        Context.Log.Error(condition.Location,
                            DiagnosticCode.InvalidSymbolInInitialCondition,
                            $"Initial proc condition can only be a PROC name; \"{funcNameObj.Name}\" is a {func.Type}");
                        return;
                    }
                    break;

                case RuleType.Query:
                    if (func.Type != FunctionType.UserQuery)
                    {
                        Context.Log.Error(condition.Location,
                            DiagnosticCode.InvalidSymbolInInitialCondition,
                            $"Initial query condition can only be a user-defined QRY name; \"{funcNameObj.Name}\" is a {func.Type}");
                        return;
                    }
                    break;

                case RuleType.Rule:
                    if (func.Type is not (FunctionType.Event or FunctionType.Database))
                    {
                        Context.Log.Error(condition.Location,
                            DiagnosticCode.InvalidSymbolInInitialCondition,
                            $"Initial rule condition can only be an event or a DB; \"{funcNameObj.Name}\" is a {func.Type}");
                        return;
                    }
                    break;

                default:
                    throw new InvalidDataException("Unknown rule type mapping target structure parameter evaluation context exception.");
            }
        }
        else
        {
            if (func.Type is not (FunctionType.SysQuery or FunctionType.Query or FunctionType.Database or FunctionType.UserQuery))
            {
                Context.Log.Error(condition.Location,
                    DiagnosticCode.InvalidFunctionTypeInCondition,
                    $"Subsequent rule conditions can only be queries or DBs; \"{funcNameObj.Name}\" is a {func.Type}");
                return;
            }
        }

        int index = 0;
        foreach (var param in func.Params)
        {
            if (index >= condition.Params.Count) break;

            var condParam = condition.Params[index];
            ValueType? type = condParam.Type;

            if (type is null)
            {
                Context.Log.Error(condParam.Location,
                    DiagnosticCode.InternalError,
                    "No type information available for func condition arg");
                continue;
            }

            VerifyIRValue(rule, condParam, func);
            VerifyIRValueCall(rule, condParam, func, index, conditionIndex, condition.Not);
            VerifyParamCompatibility(func, index, param, condParam);

            index++;
        }
    }
    private static Value.Type IntrinsicTypeToCompatibilityType(Value.Type typeId)
    {
        return typeId switch
        {
            Value.Type.Integer or Value.Type.Integer64 or Value.Type.Float => Value.Type.Integer,
            Value.Type.String or Value.Type.GuidString => Value.Type.String,
            _ => throw new ArgumentException("Cannot check compatibility of unknown types.")
        };
    }

    private static bool AreIntrinsicTypesCompatible(Value.Type type1, Value.Type type2)
    {
        return IntrinsicTypeToCompatibilityType(type1) == IntrinsicTypeToCompatibilityType(type2);
    }

    private static bool IsRiskyComparison(Value.Type type1, Value.Type type2)
    {
        return (type1 == Value.Type.String && type2 == Value.Type.GuidString)
            || (type1 == Value.Type.GuidString && type2 == Value.Type.String);
    }

    private static bool IsGuidAliasToAliasCast(ValueType type1, ValueType type2)
    {
        ArgumentNullException.ThrowIfNull(type1);
        ArgumentNullException.ThrowIfNull(type2);

        return type1.IntrinsicTypeId == type2.IntrinsicTypeId
            && type1.IntrinsicTypeId == Value.Type.GuidString
            && type1.TypeId != (uint)Value.Type.GuidString
            && type2.TypeId != (uint)Value.Type.GuidString
            && type1.TypeId != type2.TypeId;
    }

    private void VerifyIRBinaryConditionValue(IRRule rule, IRValue value, int conditionIndex)
    {
        VerifyIRValue(rule, value, null!);

        if (value is IRVariable variable)
        {
            if (variable.Index >= rule.Variables.Count) return;
            var ruleVar = rule.Variables[variable.Index];

            if (ruleVar.FirstBindingIndex == -1 || ruleVar.FirstBindingIndex >= conditionIndex)
            {
                Context.Log.Error(variable.Location,
                    DiagnosticCode.ParamNotBound,
                    "Variable {0} is not bound (when used in a binary expression)",
                    ruleVar.Name);
            }
        }
    }
    private void VerifyIRBinaryCondition(IRRule rule, IRBinaryCondition condition, int conditionIndex)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(condition);

        var lhsType = condition.LValue.Type;
        var rhsType = condition.RValue.Type;

        if ((lhsType is null && condition.LValue is IRVariable) ||
            (rhsType is null && condition.RValue is IRVariable))
        {
            return;
        }

        if (lhsType is null || rhsType is null) return;

        if (condition.LValue is IRVariable { Index: var lIndex } &&
            condition.RValue is IRVariable { Index: var rIndex } &&
            lIndex == rIndex &&
            Game == TargetGame.DOS2 &&
            rule.Goal is { Name: var goalName } &&
            !string.Equals(goalName, "EndGame_PrisonersDilemma", StringComparison.Ordinal))
        {
            Context.Log.Error(condition.Location,
                DiagnosticCode.BinaryOperationSameRhsLhs,
                "Same variable used on both sides of a binary expression; this will result in an invalid compare in runtime");
            return;
        }

        VerifyIRBinaryConditionValue(rule, condition.LValue, conditionIndex);
        VerifyIRBinaryConditionValue(rule, condition.RValue, conditionIndex);

        if (!AreIntrinsicTypesCompatible(lhsType.IntrinsicTypeId, rhsType.IntrinsicTypeId))
        {
            Context.Log.Error(condition.Location,
                DiagnosticCode.LocalTypeMismatch,
                $"Type of left expression ({TypeToName(lhsType.IntrinsicTypeId)}) differs from type of right expression ({TypeToName(rhsType.IntrinsicTypeId)})");
            return;
        }

        if (IsRiskyComparison(lhsType.IntrinsicTypeId, rhsType.IntrinsicTypeId))
        {
            Context.Log.Error(condition.Location,
                DiagnosticCode.RiskyComparison,
                $"Comparison between {TypeToName(lhsType.IntrinsicTypeId)} and {TypeToName(rhsType.IntrinsicTypeId)} may trigger incorrect behavior");
            return;
        }

        if (IsGuidAliasToAliasCast(lhsType, rhsType))
        {
            Context.Log.Error(condition.Location,
                DiagnosticCode.GuidAliasMismatch,
                $"GUID alias type of left expression ({TypeToName(lhsType.TypeId)}) differs from type of right expression ({TypeToName(rhsType.TypeId)})");
            return;
        }

        if (lhsType.IntrinsicTypeId is Value.Type.String or Value.Type.GuidString &&
            condition.Op is RelOpType.Greater or RelOpType.GreaterOrEqual or RelOpType.Less or RelOpType.LessOrEqual)
        {
            Context.Log.Warn(condition.Location,
                DiagnosticCode.StringLtGtComparison,
                $"String comparison using operator {condition.Op} - probably a mistake?");
            return;
        }
    }
    private void VerifyIRRule(IRRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Type is RuleType.Proc or RuleType.Query &&
            rule.Conditions is [IRFuncCondition initCond, ..] &&
            initCond.Func.Name is { Name: var initialNameText })
        {
            if (rule.Type == RuleType.Proc)
            {
                if (!initialNameText.StartsWith("PROC", StringComparison.OrdinalIgnoreCase))
                {
                    Context.Log.Warn(initCond.Location,
                        DiagnosticCode.RuleNamingStyle,
                        $"Name of PROC \"{initialNameText}\" should start with the prefix \"PROC\"");
                }
            }
            else if (rule.Type == RuleType.Query)
            {
                if (!initialNameText.StartsWith("QRY", StringComparison.OrdinalIgnoreCase))
                {
                    Context.Log.Warn(initCond.Location,
                        DiagnosticCode.RuleNamingStyle,
                        $"Name of Query \"{initialNameText}\" should start with the prefix \"QRY\"");
                }
            }
        }

        int index = 0;
        foreach (var condition in rule.Conditions)
        {
            switch (condition)
            {
                case IRBinaryCondition binCond:
                    VerifyIRBinaryCondition(rule, binCond, index);
                    break;

                case IRFuncCondition funcCond:
                    VerifyIRFuncCondition(rule, funcCond, index);
                    break;
            }
            index++;
        }

        foreach (var action in rule.Actions)
        {
            VerifyIRStatement(rule, action);
        }

        foreach (var variable in rule.Variables)
        {
            if (variable.Type is null)
            {
                Context.Log.Error(rule.Location,
                    DiagnosticCode.UnresolvedVariableType,
                    $"Variable \"{variable.Name}\" of rule could not be typed");
            }
        }
    }

    private void VerifyDatabases()
    {
        foreach (var signature in Context.Signatures)
        {
            if (signature.Value.Type == FunctionType.Database
                && !signature.Key.Name.StartsWith("DB_", StringComparison.OrdinalIgnoreCase))
            {
                Context.Log.Warn(null,
                    DiagnosticCode.DbNamingStyle,
                    "Name of database \"{0}\" should start with the prefix \"DB\"",
                    signature.Key.Name);
            }
        }
    }

    private void VerifyUnusedDatabases()
    {
        foreach (var signature in Context.Signatures)
        {
            if (signature.Value.Type == FunctionType.Database
                && !IgnoreUnusedDatabases.Contains(signature.Key))
            {
                Debug.Assert(signature.Value.Inserted
                    || signature.Value.Deleted
                    || signature.Value.Read);

                if (!signature.Value.Read)
                {
                    // Unused databases are considered an error in DOS:2 DE or BG3.
                    if (Game is TargetGame.DOS2DE or TargetGame.BG3)
                    {
                        Context.Log.Error(null,
                            DiagnosticCode.UnusedDatabaseError,
                            "{0} \"{1}\" is written to, but is never read",
                            signature.Value.Type, signature.Key);
                    }
                    else
                    {
                        Context.Log.Warn(null,
                            DiagnosticCode.UnusedDatabaseWarning,
                            "{0} \"{1}\" is written to, but is never read",
                            signature.Value.Type, signature.Key);
                    }
                }

                if (!signature.Value.Inserted
                    && !signature.Value.Deleted
                    && signature.Value.Read)
                {
                    // Unused databases are considered an error in DOS:2 DE or BG3.
                    if (Game is TargetGame.DOS2DE or TargetGame.BG3)
                    {
                        Context.Log.Error(null,
                            DiagnosticCode.UnusedDatabaseError,
                            "{0} \"{1}\" is read, but is never written to",
                            signature.Value.Type, signature.Key);
                    }
                    else
                    {
                        Context.Log.Warn(null,
                            DiagnosticCode.UnusedDatabaseWarning,
                            "{0} \"{1}\" is read, but is never written to",
                            signature.Value.Type, signature.Key);
                    }
                }

                if (!signature.Value.Inserted
                    && signature.Value.Deleted
                    && signature.Value.Read)
                {
                    Context.Log.Warn(null,
                        DiagnosticCode.UnwrittenDatabase,
                        "{0} \"{1}\" is read and deleted, but is never inserted into",
                        signature.Value.Type, signature.Key);
                }
            }
        }
    }

    public void VerifyIR(IEnumerable<IRGoal> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);

        foreach (var goal in goals)
        {
            if (goal is null) continue;

            foreach (var parentGoal in goal.ParentTargetEdges)
            {
                if (parentGoal?.Goal?.Name is null) continue;

                if (Context.LookupGoal(parentGoal.Goal.Name) is null)
                {
                    Context.Log.Error(parentGoal.Location,
                        DiagnosticCode.UnresolvedGoal,
                        "Parent goal of \"{0}\" could not be resolved: \"{1}\"",
                        goal.Name, parentGoal.Goal.Name);
                }
            }

            foreach (var fact in goal.InitSection)
            {
                if (fact is not null) VerifyIRFact(fact);
            }

            foreach (var rule in goal.KBSection)
            {
                if (rule is not null) VerifyIRRule(rule);
            }

            foreach (var fact in goal.ExitSection)
            {
                if (fact is not null) VerifyIRFact(fact);
            }
        }

        // Validate database names
        // We do this here as there is no explicit declaration for databases,
        // they are created implicitly on first use.
        VerifyDatabases();
        VerifyUnusedDatabases();
    }

    private ValueType? ConstantTypeToValueType(IRConstantType type)
    {
        return type switch
        {
            IRConstantType.Unknown => null,
            IRConstantType.Integer => Context.TypesById.TryGetValue(1, out var t1) ? t1 : null,
            IRConstantType.Float => Context.TypesById.TryGetValue(3, out var t3) ? t3 : null,
            IRConstantType.String => Context.TypesById.TryGetValue(4, out var t4) ? t4 : null,
            IRConstantType.Name => Context.TypesById.TryGetValue(5, out var t5) ? t5 : null,
            _ => throw new ArgumentException("Invalid IR constant type definition.")
        };
    }

    private ValueType? DetermineSignature(IRConstant value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Type is not null)
        {
            return Context.LookupType(value.Type.Name);
        }

        return ConstantTypeToValueType((IRConstantType)value.GetType().GetProperty("ValueType")?.GetValue(value)!);
    }

    private ValueType? DetermineSignature(IRRule rule, IRValue value)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(value);

        if (value is IRConstant constant)
        {
            return DetermineSignature(constant);
        }

        if (value is IRVariable variable)
        {
            if (variable.Type is not null)
            {
                return variable.Type;
            }

            if (variable.Index >= rule.Variables.Count) return null;
            var ruleVar = rule.Variables[variable.Index];

            return ruleVar.Type ?? null;
        }

        throw new ArgumentException("Invalid IR value type mapping scenario exception.");
    }

    private bool ApplySignature(FunctionNameAndArity name, FunctionType? type, List<ValueType?> paramTypes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(paramTypes);

        var registeredSignature = Context.LookupSignature(name);
        var signature = registeredSignature;

        if (signature is not null && signature.FullyTyped)
        {
            throw new InvalidOperationException("Cannot apply signature to an already typed name identifier block.");
        }

        if (signature is null)
        {
            signature = new FunctionSignature
            {
                Name = name.Name,
                Type = type ?? FunctionType.Database,
                Inserted = false,
                Deleted = false,
                Read = false
            };
        }
        else
        {
            if (type is not null && signature.Type != type)
            {
                Context.Log.Error(null,
                    DiagnosticCode.ProcTypeMismatch,
                    "Auto-typing name {0}: first seen as {1}, now seen as {2}",
                    name, signature.Type, type);
            }
        }

        bool hasNullTypes = false;
        for (int i = 0; i < paramTypes.Count; i++)
        {
            if (paramTypes[i] is null)
            {
                hasNullTypes = true;
                break;
            }
        }
        signature.FullyTyped = !hasNullTypes;

        signature.Params = new List<FunctionParam>(paramTypes.Count);
        foreach (var paramType in paramTypes)
        {
            var sigParam = new FunctionParam
            {
                Type = paramType ?? new ValueType { Name = "UNKNOWN" },
                Direction = ParamDirection.In,
                Name = string.Empty
            };
            signature.Params.Add(sigParam);
        }

        if (registeredSignature is null)
        {
            Context.RegisterFunction(signature, null!);
        }

        return signature.FullyTyped;
    }

    private bool TryPropagateSignature(IRRule rule, FunctionNameAndArity name, FunctionType? type, List<IRValue> parameters,
        bool allowPartial, ref bool updated)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Build a signature with all parameters to make sure that all types can be resolved
        var sig = new List<ValueType?>(parameters.Count);
        foreach (var param in parameters)
        {
            if (param is null) continue;
            var paramSignature = DetermineSignature(rule, param);
            if (paramSignature is not null)
            {
                sig.Add(paramSignature);
            }
            else
            {
                if (allowPartial)
                {
                    sig.Add(null);
                }
                else
                {
                    return false;
                }
            }
        }

        // Apply signature to symbol
        updated = true;
        return ApplySignature(name, type, sig);
    }
    private bool PropagateSignature(FunctionNameAndArity name, FunctionType? type, List<IRConstant> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Build a signature with all parameters to make sure that all types can be resolved
        var sig = new List<ValueType?>(parameters.Count);
        foreach (var param in parameters)
        {
            if (param is null) continue;
            var paramSignature = DetermineSignature(param);
            sig.Add(paramSignature);
        }

        // Apply signature to symbol
        ApplySignature(name, type, sig);
        return true;
    }

    private bool PropagateSignatureIfRequired(IRRule rule, FunctionNameAndArity name, FunctionType? type, List<IRValue> parameters, bool allowPartial, ref bool updated)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(name);

        var signature = Context.LookupSignature(name);
        bool signatureOk = signature is not null && signature.FullyTyped;

        if (!signatureOk && TryPropagateSignature(rule, name, type, parameters, allowPartial, ref updated))
        {
            signature = Context.LookupSignature(name);
            signatureOk = signature is not null && signature.FullyTyped;
        }

        if (signatureOk && signature is not null)
        {
            if (PropagateRuleTypesFromParamList(rule, parameters, signature))
            {
                updated = true;
            }
        }

        return signatureOk;
    }

    private bool PropagateSignatureIfRequired(FunctionNameAndArity name, FunctionType? type, List<IRConstant> parameters, ref bool updated)
    {
        ArgumentNullException.ThrowIfNull(name);

        var signature = Context.LookupSignature(name);
        if (signature is null || !signature.FullyTyped)
        {
            updated = true;
            return PropagateSignature(name, type, parameters);
        }

        return true;
    }

    private static bool PropagateIRVariableType(IRRule rule, IRVariable variable, ValueType type)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(type);

        bool updated = false;
        if (variable.Index >= rule.Variables.Count) return false;

        var ruleVar = rule.Variables[variable.Index];
        if (ruleVar.Type is null)
        {
            ruleVar.Type = type;
            updated = true;
        }

        if (variable.Type is null)
        {
            if (ruleVar.Type.IsAliasOf(type))
            {
                variable.Type = ruleVar.Type;
            }
            else
            {
                variable.Type = type;
            }

            updated = true;
        }

        return updated;
    }

    private static bool PropagateRuleTypesFromParamList(IRRule rule, List<IRValue> parameters, FunctionSignature signature)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(signature);

        bool updated = false;
        int index = 0;

        foreach (var param in parameters)
        {
            if (param is IRVariable variable)
            {
                if (index >= signature.Params.Count) break;
                if (PropagateIRVariableType(rule, variable, signature.Params[index].Type))
                {
                    updated = true;
                }
            }

            index++;
        }

        return updated;
    }

    private bool PropagateRuleTypes(IRFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        bool updated = false;

        if (fact.Database is not null && fact.Database.Name is not null)
        {
            var constantsList = new List<IRConstant>(fact.Elements.Count);
            for (int i = 0; i < fact.Elements.Count; i++)
            {
                if (fact.Elements[i] is IRConstant constant)
                {
                    constantsList.Add(constant);
                }
            }
            if (PropagateSignatureIfRequired(fact.Database.Name, FunctionType.Database, constantsList, ref updated))
            {
                // Core type sync parameters matched successfully
            }
        }

        return updated;
    }

    private static bool PropagateRuleTypes(IRRule rule, IRBinaryCondition condition)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(condition);

        bool updated = false;

        if (condition.LValue.Type is null && condition.LValue is IRVariable lval)
        {
            if (lval.Index < rule.Variables.Count)
            {
                var ruleVariable = rule.Variables[lval.Index];
                if (ruleVariable.Type is not null)
                {
                    lval.Type = ruleVariable.Type;
                    updated = true;
                }
            }
        }

        if (condition.RValue.Type is null && condition.RValue is IRVariable rval)
        {
            if (rval.Index < rule.Variables.Count)
            {
                var ruleVariable = rule.Variables[rval.Index];
                if (ruleVariable.Type is not null)
                {
                    rval.Type = ruleVariable.Type;
                    updated = true;
                }
            }
        }

        if (condition.LValue is IRVariable lVar && lVar.Index < rule.Variables.Count)
        {
            var leftRuleVar = rule.Variables[lVar.Index];
            if (leftRuleVar.Type is null && condition.RValue.Type is not null)
            {
                leftRuleVar.Type = condition.RValue.Type;
                lVar.Type = condition.RValue.Type;
                updated = true;
            }
        }
        if (condition.RValue is IRVariable rVar && rVar.Index < rule.Variables.Count)
        {
            var rightRuleVar = rule.Variables[rVar.Index];
            if (rightRuleVar.Type is null && condition.LValue.Type is not null)
            {
                rightRuleVar.Type = condition.LValue.Type;
                rVar.Type = condition.LValue.Type;
                updated = true;
            }
        }

        return updated;
    }

    private static int ComputeTupleSize(IRRule rule, IRFuncCondition condition, int lastTupleSize)
    {
        _ = rule;
        ArgumentNullException.ThrowIfNull(condition);

        int tupleSize = lastTupleSize;
        foreach (var param in condition.Params)
        {
            if (param is IRVariable { Index: var index })
            {
                tupleSize = Math.Max(tupleSize, index + 1);
            }
        }

        return tupleSize;
    }

    private static int ComputeTupleSize(IRRule rule, IRBinaryCondition condition, int lastTupleSize)
    {
        _ = rule;
        ArgumentNullException.ThrowIfNull(condition);

        int tupleSize = lastTupleSize;

        if (condition.LValue is IRVariable { Index: var lIndex })
        {
            tupleSize = Math.Max(tupleSize, lIndex + 1);
        }

        if (condition.RValue is IRVariable { Index: var rIndex })
        {
            tupleSize = Math.Max(tupleSize, rIndex + 1);
        }

        return tupleSize;
    }

    private bool PropagateRuleTypes(IRRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        bool updated = false;

        int lastTupleSize = 0;
        foreach (var condition in rule.Conditions)
        {
            if (condition is null) continue;

            int currentTupleSize = condition switch
            {
                IRFuncCondition f => f.TupleSize,
                IRBinaryCondition b => b.TupleSize,
                _ => -1
            };

            if (condition is IRFuncCondition func && func.Func is { Name: { } funcNameObj })
            {
                _ = PropagateSignatureIfRequired(rule, new(funcNameObj.Name, func.Params.Count), null, func.Params, false, ref updated);

                if (currentTupleSize == -1)
                {
                    currentTupleSize = ComputeTupleSize(rule, func, lastTupleSize);

                    func.TupleSize = currentTupleSize;
                    updated = true;
                }
            }
            else if (condition is IRBinaryCondition bin)
            {
                if (PropagateRuleTypes(rule, bin))
                {
                    updated = true;
                }

                if (currentTupleSize == -1)
                {
                    currentTupleSize = ComputeTupleSize(rule, bin, lastTupleSize);

                    bin.TupleSize = currentTupleSize;
                    updated = true;
                }
            }

            lastTupleSize = currentTupleSize;
        }

        foreach (var action in rule.Actions)
        {
            if (action is { Func.Name: { } actionNameObj, Params: var actionParams })
            {
                _ = PropagateSignatureIfRequired(rule, new(actionNameObj.Name, actionParams.Count), null, actionParams, false, ref updated);
            }
        }

        return updated;
    }

    public bool PropagateRuleTypes(IEnumerable<IRGoal> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        bool updated = false;

        foreach (var goal in goals)
        {
            if (goal is null) continue;

            foreach (var fact in goal.InitSection)
            {
                if (fact is not null && PropagateRuleTypes(fact))
                {
                    updated = true;
                }
            }

            foreach (var rule in goal.KBSection)
            {
                if (rule is not null && PropagateRuleTypes(rule))
                {
                    updated = true;
                }
            }

            foreach (var fact in goal.ExitSection)
            {
                if (fact is not null && PropagateRuleTypes(fact))
                {
                    updated = true;
                }
            }
        }

        return updated;
    }
    private void AddQueryOrProc(IRRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Conditions is { Count: 0 })
        {
            Context.Log.Error(rule.Location,
                DiagnosticCode.InvalidProcDefinition,
                $"Declaration of a {rule.Type} cannot be empty.");
            return;
        }

        var procDefn = rule.Conditions[0];

        if (procDefn is IRFuncCondition def && def.Func.Name is { } funcNameObj)
        {
            FunctionType type = rule.Type switch
            {
                RuleType.Proc => FunctionType.Proc,
                RuleType.Query => FunctionType.UserQuery,
                _ => throw new InvalidOperationException("Cannot register this type as a PROC or QUERY configuration mapping.")
            };

            bool updated = false;

            if (!PropagateSignatureIfRequired(rule, new(funcNameObj.Name, def.Params.Count), type, def.Params, allowPartial: true, ref updated))
            {
                Context.Log.Warn(procDefn.Location,
                    DiagnosticCode.UnresolvedSignature,
                    $"Signature could not be completely typed or matched in declaration of {rule.Type} \"{funcNameObj.Name}\"");
            }
        }
        else
        {
            Context.Log.Error(procDefn.Location,
                DiagnosticCode.InvalidProcDefinition,
                $"Declaration of a {rule.Type} must start with a {rule.Type} name and signature.");
        }
    }

    public void AddGoal(IRGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        Context.RegisterGoal(goal);
        foreach (var rule in goal.KBSection)
        {
            if (rule is null) continue;
            if (rule.Type is RuleType.Query or RuleType.Proc)
            {
                AddQueryOrProc(rule);
            }
        }
    }
}